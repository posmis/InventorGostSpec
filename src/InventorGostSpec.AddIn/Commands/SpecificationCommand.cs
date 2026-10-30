using InventorGostSpec.AddIn.Infrastructure;
using InventorGostSpec.AddIn.InventorApi;
using InventorGostSpec.Core.Model;
using InventorGostSpec.Core.Storage;
using InventorGostSpec.Core.Update;
using InventorGostSpec.Core.Versions;
using InventorGostSpec.UI.ViewModels;
using InventorGostSpec.UI.Views;

namespace InventorGostSpec.AddIn.Commands;

/// <summary>
/// Окно спецификации сборки. Если спецификации ещё нет, она формируется по составу сборки.
/// Дальше спецификация меняется только по командам пользователя в окне.
/// </summary>
internal sealed class SpecificationCommand : AddInCommand
{
    public const string Id = "InventorGostSpec.Specification";

    public override string InternalName => Id;

    public override string DisplayName => "Спецификация";

    public override string Description => "Открыть спецификацию сборки по ГОСТ 2.106";

    public override string IconName => "specification";

    public override void Execute(AddInContext context)
    {
        var assembly = context.RequireAssembly();
        if (assembly is null)
        {
            return;
        }

        var host = new InventorSpecificationHost(context, assembly);
        var specification = SpecificationStore.Load((Document)assembly, out var fromBackup);
        if (fromBackup)
        {
            context.Messages.Warning(
                "Сохранённая спецификация повреждена, открыта предыдущая сохранённая версия.\n" +
                "Проверьте последние изменения и сохраните спецификацию.");
        }

        var isNew = specification is null;
        if (specification is null)
        {
            // Первое формирование ничего не перезаписывает, поэтому выполняется сразу.
            specification = new Specification();
            var specContext = SpecificationContext.Create(host.LoadSettings(), specification);
            SpecificationUpdater.Update(specification, host.ReadComponents(), specContext, DateTime.UtcNow);
        }

        // Восстановленную из копии спецификацию предлагаем сохранить при закрытии, как новую.
        SpecificationWindow.ShowDialog(host, specification, context.MainWindowHandle, isNew || fromBackup);
    }

    private sealed class InventorSpecificationHost : ISpecificationHost
    {
        private readonly AddInContext _context;
        private readonly AssemblyDocument _assembly;

        public InventorSpecificationHost(AddInContext context, AssemblyDocument assembly)
        {
            _context = context;
            _assembly = assembly;
        }

        public string DocumentName => ((Document)_assembly).DisplayName;

        public string AssemblyFolder => BomAccess.AssemblyFolder(_assembly);

        public SpecSettings LoadSettings() => _context.Settings.Load();

        public void SaveSettings(SpecSettings settings) => _context.Settings.Save(settings);

        public IReadOnlyList<ComponentData> ReadComponents() => AssemblyReader.Read(_context.Application, _assembly);

        public void Save(Specification specification)
        {
            var document = (Document)_assembly;
            using var transaction = new InventorTransaction(_context.Application, document, "Сохранить спецификацию");
            SpecificationStore.Save(document, specification);
            transaction.Commit();
        }

        public string WritePositions(Specification specification)
        {
            using var transaction = new InventorTransaction(_context.Application, (Document)_assembly, "Позиции в BOM");
            var report = PositionWriter.Write(_assembly, specification);
            transaction.Commit();
            return report + "\n\nСохраните сборку, чтобы изменения записались в файл.";
        }

        public IReadOnlyDictionary<string, string?>? ReadComponentProperties(string componentKey)
        {
            var item = FindItem(componentKey);
            return item is null
                ? null
                : ComponentTarget.SpecProperties.ToDictionary(name => name, name => (string?)item.Target.Get(name));
        }

        public ComponentData WriteComponentProperties(string componentKey, IReadOnlyDictionary<string, string> changes)
        {
            var item = FindItem(componentKey) ?? throw new InvalidOperationException("Компонент не найден в сборке.");
            using (var transaction = new InventorTransaction(_context.Application, (Document)_assembly, "Свойства компонента"))
            {
                foreach (var (name, value) in changes)
                {
                    item.Target.Set(name, value);
                }

                transaction.Commit();
            }

            var drawings = item.Target.Kind == ComponentKind.Assembly
                ? new DrawingSearch(_context.Application, new[] { System.IO.Path.GetDirectoryName(item.Target.FullFileName ?? "") ?? "" })
                : null;
            return AssemblyReader.ToComponentData(item.Target, item.Key, AssemblyReader.Quantity(item.Row), drawings);
        }

        public string? ReadComponentNote(string componentKey)
        {
            if (FindItem(componentKey) is not { } item)
            {
                return null;
            }

            var propertyName = _context.Settings.Load().NotePropertyName;
            var note = string.IsNullOrWhiteSpace(propertyName) ? "" : item.Target.Get(propertyName);
            return note.Length > 0
                ? note
                : PropertyAccess.Get(item.Target.PropertySets, PropertyAccess.SummarySet, PropertyAccess.Comments);
        }

        public ExportInfo GetExportInfo()
        {
            var document = (Document)_assembly;
            var sets = document.PropertySets;
            string Get(string set, string name) => PropertyAccess.TryGet(sets, set, name);

            var designation = Designations.WithoutAssemblyDrawingCode(Get(PropertyAccess.DesignTrackingSet, PropertyAccess.PartNumber));
            var name = Get(PropertyAccess.DesignTrackingSet, PropertyAccess.Description);
            var title = new TitleBlock
            {
                Designation = designation,
                Name = name.Length > 0 ? name : Get(PropertyAccess.SummarySet, PropertyAccess.Title),
                // Как в ГОСТ-шаблонах чертежей Inventor: «Утвердил (инженер)» — нормоконтроль,
                // «Утвердил (технолог)» — утверждающий. Строка «Т. контр.» в спецификации пустая.
                Developer = Get(PropertyAccess.DesignTrackingSet, PropertyAccess.Designer),
                Checker = Get(PropertyAccess.DesignTrackingSet, PropertyAccess.CheckedBy),
                NormControl = Get(PropertyAccess.DesignTrackingSet, PropertyAccess.EngineeringApprovedBy),
                Approver = Get(PropertyAccess.DesignTrackingSet, PropertyAccess.ManufacturingApprovedBy),
                Organization = Get(PropertyAccess.DocumentSummarySet, PropertyAccess.Company),
                FirstUse = PropertyAccess.GetCustom(sets, PropertyNames.FirstUse),
            };

            var folder = System.IO.Path.GetDirectoryName(document.FullFileName) ?? "";
            var baseName = designation.Length > 0 ? designation : System.IO.Path.GetFileNameWithoutExtension(document.FullFileName);
            var fileName = string.Concat($"{baseName} Спецификация.xlsx".Split(System.IO.Path.GetInvalidFileNameChars()));
            return new ExportInfo(title, System.IO.Path.Combine(folder, fileName));
        }

        public VersionData? ReadVersion(string path)
        {
            var full = System.IO.Path.GetFullPath(path);
            if (string.Equals(full, ((Document)_assembly).FullFileName, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException("Это сама головная сборка.");
            }

            var needsMark = false;
            var data = WithDocument(full, save: false, document =>
            {
                if (document is not AssemblyDocument version)
                {
                    return null;
                }

                // Пометка «исполнение сборки …» с путём к головной (старые пометки — без пути) — дописывается ниже.
                var headFile = System.IO.Path.GetRelativePath(BomAccess.AssemblyFolder(version), ((Document)_assembly).FullFileName);
                needsMark = !string.Equals(
                    PropertyAccess.TryGet(document.PropertySets, PropertyAccess.UserDefinedSet, PropertyNames.VersionOfFile), headFile,
                    StringComparison.OrdinalIgnoreCase);

                var headFolder = BomAccess.AssemblyFolder(_assembly);
                var versionFolder = BomAccess.AssemblyFolder(version);
                string Rebase(string key) => VersionSync.RebaseKey(key, versionFolder, headFolder);

                var components = AssemblyReader.Read(_context.Application, version);
                foreach (var component in components)
                {
                    component.Key = Rebase(component.Key);
                }

                // Группы «устанавливаемые по …» из спецификации самой сборки-исполнения (если она там сформирована).
                var groupTitles = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                if (SpecificationStore.Load(document) is { } own)
                {
                    var titles = own.Groups.Where(g => !g.IsVersion && g.VersionId.Length == 0).ToDictionary(g => g.Id, g => g.Title);
                    foreach (var row in own.Rows.Where(r => r.Kind == RowKind.Component && r.ComponentKey is not null))
                    {
                        if (titles.TryGetValue(row.GroupId, out var title))
                        {
                            groupTitles[Rebase(row.ComponentKey!)] = title;
                        }
                    }
                }

                var partNumber = PropertyAccess.TryGet(document.PropertySets, PropertyAccess.DesignTrackingSet, PropertyAccess.PartNumber);
                return new VersionData(Designations.WithoutAssemblyDrawingCode(partNumber), components, groupTitles);
            });

            if (data is not null && needsMark)
            {
                try
                {
                    var head = PropertyAccess.TryGet(((Document)_assembly).PropertySets, PropertyAccess.DesignTrackingSet, PropertyAccess.PartNumber);
                    MarkVersion(full, Designations.WithoutAssemblyDrawingCode(head));
                }
                catch (Exception ex)
                {
                    // Файл только для чтения — номера позиций в исполнении взять не получится, но чтение не прерываем.
                    Log.Error("MarkVersion " + full, ex);
                }
            }

            return data;
        }

        public void MarkVersion(string path, string headDesignation)
        {
            var head = ((Document)_assembly).FullFileName;
            var full = System.IO.Path.GetFullPath(path);
            var value = $"{headDesignation} ({System.IO.Path.GetFileName(head)})";
            var headFile = System.IO.Path.GetRelativePath(System.IO.Path.GetDirectoryName(full) ?? "", head);
            WithDocument(full, save: true, document =>
            {
                PropertyAccess.SetCustom(document.PropertySets, PropertyNames.VersionOf, value);
                PropertyAccess.SetCustom(document.PropertySets, PropertyNames.VersionOfFile, headFile);
                return (object?)null;
            });
        }

        public void UnmarkVersion(string path) =>
            WithDocument(System.IO.Path.GetFullPath(path), save: true, document =>
            {
                PropertyAccess.SetCustom(document.PropertySets, PropertyNames.VersionOf, "");
                PropertyAccess.SetCustom(document.PropertySets, PropertyNames.VersionOfFile, "");
                return (object?)null;
            });

        public IReadOnlyDictionary<string, int>? ReadHeadPositions()
        {
            var own = (Document)_assembly;
            var headFile = PropertyAccess.TryGet(own.PropertySets, PropertyAccess.UserDefinedSet, PropertyNames.VersionOfFile);
            var ownFolder = BomAccess.AssemblyFolder(_assembly);
            if (headFile.Length == 0)
            {
                throw new InvalidOperationException(
                    "В сборке нет пути к головной сборке (пометка записана старой версией аддина). " +
                    "Откройте головную сборку и обновите её спецификацию — пометка запишется заново.");
            }

            var headPath = System.IO.Path.GetFullPath(System.IO.Path.Combine(ownFolder, headFile));
            if (!System.IO.File.Exists(headPath))
            {
                throw new InvalidOperationException($"Головная сборка не найдена:\n{headPath}");
            }

            return WithDocument(headPath, save: false, document =>
            {
                if (document is not AssemblyDocument head || SpecificationStore.Load(document) is not { } spec)
                {
                    throw new InvalidOperationException("В головной сборке нет спецификации.");
                }

                var headFolder = BomAccess.AssemblyFolder(head);
                var version = VersionSync.Versions(spec).FirstOrDefault(g => !g.IsBaseVersion && string.Equals(
                    System.IO.Path.GetFullPath(System.IO.Path.Combine(headFolder, g.VersionFile)), own.FullFileName, StringComparison.OrdinalIgnoreCase));
                if (version is null)
                {
                    throw new InvalidOperationException("В групповой спецификации головной сборки этого исполнения нет.");
                }

                return (IReadOnlyDictionary<string, int>)VersionSync.PositionsFor(spec, version)
                    .ToDictionary(x => VersionSync.RebaseKey(x.Key, headFolder, ownFolder), x => x.Value, StringComparer.OrdinalIgnoreCase);
            });
        }

        public string? VersionOf
        {
            get
            {
                var sets = ((Document)_assembly).PropertySets;
                var head = PropertyAccess.TryGet(sets, PropertyAccess.UserDefinedSet, PropertyNames.VersionOf);
                if (head.Length == 0)
                {
                    return null;
                }

                var own = Designations.WithoutAssemblyDrawingCode(PropertyAccess.TryGet(sets, PropertyAccess.DesignTrackingSet, PropertyAccess.PartNumber));
                return $"Эта сборка — исполнение {own} сборки {head}: её переменные данные показываются в групповой спецификации " +
                       "головной сборки. Здесь можно работать как обычно.";
            }
        }

        /// <summary>
        /// Документ, открытый в Inventor, или открытый невидимо на время действия. Открытый невидимо при
        /// <paramref name="save"/> сохраняется и закрывается; открытый пользователем — только меняется (сохранит сам).
        /// </summary>
        private T? WithDocument<T>(string path, bool save, Func<Document, T?> action)
        {
            foreach (Document open in _context.Application.Documents)
            {
                if (string.Equals(open.FullFileName, path, StringComparison.OrdinalIgnoreCase))
                {
                    return action(open);
                }
            }

            var document = _context.Application.Documents.Open(path, false);
            try
            {
                var result = action(document);
                if (save)
                {
                    document.Save();
                }

                return result;
            }
            finally
            {
                document.Close(true);
            }
        }

        public void WriteTitleProperties(TitleBlock title)
        {
            var document = (Document)_assembly;
            var sets = document.PropertySets;
            using var transaction = new InventorTransaction(_context.Application, document, "Основная надпись");
            PropertyAccess.Set(sets, PropertyAccess.DesignTrackingSet, PropertyAccess.Designer, title.Developer);
            PropertyAccess.Set(sets, PropertyAccess.DesignTrackingSet, PropertyAccess.CheckedBy, title.Checker);
            PropertyAccess.Set(sets, PropertyAccess.DesignTrackingSet, PropertyAccess.EngineeringApprovedBy, title.NormControl);
            PropertyAccess.Set(sets, PropertyAccess.DesignTrackingSet, PropertyAccess.ManufacturingApprovedBy, title.Approver);
            PropertyAccess.Set(sets, PropertyAccess.DocumentSummarySet, PropertyAccess.Company, title.Organization);
            PropertyAccess.SetCustom(sets, PropertyNames.FirstUse, title.FirstUse);
            transaction.Commit();
        }

        public string GetAutoSectionId(string componentKey) =>
            FindItem(componentKey) is { } item
                ? SectionResolver.GetDefault(item.Target.Kind, item.Target.IsContentCenter)
                : "";

        public string? BuildPdf(string excelPath, string pdfPath) => PdfToolRunner.Run(excelPath, pdfPath);

        public IReadOnlyDictionary<string, DrawingFormat> FindDrawingFormats(IReadOnlyList<string> componentKeys)
        {
            var keys = componentKeys.ToHashSet(StringComparer.OrdinalIgnoreCase);
            var result = new Dictionary<string, DrawingFormat>(StringComparer.OrdinalIgnoreCase);
            var items = BomAccess.GetFirstLevel(_assembly).Where(i => keys.Contains(i.Key)).ToList();
            var search = new DrawingSearch(
                _context.Application,
                items.Select(i => System.IO.Path.GetDirectoryName(i.Target.FullFileName ?? "") ?? "").Distinct());
            foreach (var item in items)
            {
                if (result.ContainsKey(item.Key) || item.Target.FullFileName is not { } file)
                {
                    continue;
                }

                var drawing = search.Find(file, item.Target.PartNumber, item.Target.Kind == ComponentKind.Assembly);
                result[item.Key] = drawing is null
                    ? new DrawingFormat(null, "")
                    : new DrawingFormat(drawing, DrawingAccess.ReadFormat(_context.Application, drawing));
            }

            return result;
        }

        public string? GetComponentFile(string componentKey)
        {
            const string drawingPrefix = "drawing:";
            if (componentKey.StartsWith(drawingPrefix, StringComparison.Ordinal))
            {
                return System.IO.Path.GetFullPath(System.IO.Path.Combine(BomAccess.AssemblyFolder(_assembly), componentKey[drawingPrefix.Length..]));
            }

            return FindItem(componentKey)?.Target.FullFileName;
        }

        public IReadOnlyDictionary<string, string> ReadBomItemNumbers() =>
            BomAccess.GetFirstLevel(_assembly)
                .GroupBy(item => item.Key, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(g => g.Key, g => g.First().Row.ItemNumber ?? "", StringComparer.OrdinalIgnoreCase);

        public string? OpenPdfSettings(string? excelPath) => PdfToolRunner.OpenWindow(excelPath);

        public void ReportError(Exception exception) => Log.Error("SpecificationWindow", exception);

        private BomItem? FindItem(string componentKey) =>
            BomAccess.GetFirstLevel(_assembly)
                .FirstOrDefault(i => string.Equals(i.Key, componentKey, StringComparison.OrdinalIgnoreCase));
    }
}
