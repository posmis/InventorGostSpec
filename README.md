# InventorGostSpec

Аддин для Autodesk Inventor 2025, формирующий спецификацию по ГОСТ 2.106-2019.

Возможности:

- назначение компонентам категорий (разделов спецификации) через iProperties, в том числе автоматически для стандартных изделий из Content Center;
- формирование спецификации по первому уровню сборки со строгой сортировкой по ГОСТ;
- ручное редактирование порядка, разделов, примечаний и любых колонок;
- обновление только по команде пользователя, с сохранением ручных правок;
- резерв номеров позиций между разделами;
- запись позиций в BOM Inventor.

Статус: в разработке. Подробные требования — в [docs/REQUIREMENTS.md](docs/REQUIREMENTS.md).

## Требования

- Autodesk Inventor 2025
- .NET 8 SDK
- Visual Studio 2022

## Структура

| Проект | Назначение |
|---|---|
| `src/InventorGostSpec.Core` | Модель спецификации, сортировка по ГОСТ 2.106, позиции с резервом, обновление и ручное редактирование. Не зависит от Inventor. |
| `tests/InventorGostSpec.Core.Tests` | Тесты для `Core` (xUnit). |

Сборка и тесты:

```
dotnet build
dotnet test
```

## iProperties компонентов

Обозначение и наименование берутся из стандартных `Part Number` и `Description`. Дополнительно используются пользовательские свойства:

| Свойство | Значение |
|---|---|
| `SPEC_Section` | Раздел: `Documentation`, `Complexes`, `AssemblyUnits`, `Parts`, `StandardProducts`, `OtherProducts`, `Materials`, `Kits` или id пользовательского раздела |
| `SPEC_Format` | Формат |
| `SPEC_Unit` | Единица измерения |
| `SPEC_Group` | Однородная группа (стандартные и прочие изделия) |
| `SPEC_SortName` | Наименование изделия для сортировки («Болт») |
| `SPEC_StandardNumber` | Обозначение стандарта («ГОСТ 7798-70») |
| `SPEC_Parameters` | Основные параметры («М12х60») |
| `SPEC_MaterialKind` | Вид материала: `FerrousMetals`, `MagneticMetals`, `NonFerrousMetals`, `CablesWires`, `Plastics`, `PaperTextile`, `Wood`, `RubberLeather`, `MineralCeramicGlass`, `PaintsChemicals`, `Other` |

Если `SPEC_SortName`, `SPEC_StandardNumber`, `SPEC_Parameters` не заполнены, они берутся из разбора наименования («Болт М12х60 ГОСТ 7798-70»).
