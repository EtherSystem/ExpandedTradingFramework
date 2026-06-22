# Expanded Trading Framework

Expanded Trading Framework is a modder-facing framework for The Long Dark that allows other mods to add custom exchanges to Sutherland's Trader stock.

It does not add a stock list by itself.  
Instead, it gives other mods a clean way to register new Trader trades, special requests, custom requirements, rewards, and completion behavior.

The goal is simple: make the Trader easier to expand without every content mod needing to patch the Trader UI and save system on its own.

## Overview

Expanded Trading Framework is built around `CustomTradeDefinition`.

A content mod creates one or more trade definitions, registers them through `CustomTradeRegistry`, and ETF takes care of turning them into runtime Trader exchanges.

ETF handles:

- Normal Trader exchanges.
- Custom special requests.
- Gear, category, weight, and liquid requirements.
- Gear rewards.
- Completion-only trades.
- Repeat limits inside one active trade.
- Repeatable or unique trade behavior.
- Trust-gated trades.
- Draw pool or appearance chance selection.
- Stackable reward handling.
- Active trade save/load behavior.
- Custom Trader journal special-request display and allow a possible second special-request.

This framework is meant for other modders.  
If you are a player, you normally only need this mod because another mod depends on it.

## Main Features

Expanded Trading Framework currently includes:

- [Normal Trader Exchanges](#normal-trader-exchanges)
- [Trade Selection](#trade-selection)
- [Supported Requirements](#supported-requirements)
- [Rewards](#rewards)
- [Completion-Only Trades](#completion-only-trades)
- [Special Requests](#special-requests)
- [Save Data](#save-data)
- [Settings](#settings)
- [For Developers](#for-developers)

## Normal Trader Exchanges

Normal custom trades are added to Sutherland's regular trade stock.

They can behave like vanilla stock by using draw pools, or they can use a direct appearance chance roll.

A normal trade can define:

| Field | Behavior |
|---|---|
| `Id` | Unique identifier for this custom trade. |
| `Enabled` | Allows a content mod to disable a trade before registration. |
| `SelectionMode` | Chooses between draw pool selection and direct appearance chance. |
| `DrawPool` | Vanilla-style stock pool used when `SelectionMode` is `DrawPool`. |
| `AppearanceChance` | Direct normalized roll used when `SelectionMode` is `AppearanceChance`. |
| `MinTrust` | Minimum Trader trust required for the trade to appear. |
| `MaxPerTrade` | Maximum number of times the same exchange can be repeated in one active trade. |
| `Repeatable` | If false, the trade is completed only once per save. |
| `CostItems` | Requirements the player must place in the Exchange Box. |
| `DisplayReward` | What the Trader UI displays as the gained item. |
| `Reward` | What is actually spawned when the trade is completed. |
| `OnCompleted` | Optional callback invoked after the custom trade completes. |

Trade IDs should remain stable between releases.  
Changing the meaning of an existing ID can affect old saves, especially when using non-repeatable or completion-count-based behavior.

## Trade Selection

ETF supports two selection modes.

### Draw Pool

`CustomTradeSelectionMode.DrawPool` uses `CustomTradeDrawPool`.

Available draw pools are:

- `AlwaysShown`
- `Common`
- `Rare`
- `ExtraRare`

This is the closest behavior to vanilla Trader stock selection.

### Appearance Chance

`CustomTradeSelectionMode.AppearanceChance` ignores the draw pool and uses `AppearanceChance` directly.

`AppearanceChance` is a normalized value, so `1.0f` means the trade should be selected whenever it is otherwise eligible.

This is useful when a content mod wants explicit control instead of vanilla-style stock pools.

## Supported Requirements

ETF supports several requirement types through `CustomTradeItem.ExchangeType`.

| Requirement type | Behavior |
|---|---|
| `GearAmount` | Requires a specific number of one exact item. |
| `GearWeight` | Requires a specific weight of one exact item. |
| `CategoryAmount` | Requires a number of items from a custom category. |
| `CategoryWeight` | Requires a weight from a custom category. |
| `Liquid` | Requires a volume of a supported liquid. |

Supported liquid kinds are:

- `PotableWater`
- `NonPotableWater`
- `Kerosene`
- `Antiseptic`

Category requirements can use a custom list of allowed gear names.  
Their requirement icon can use the first allowed item, one specific gear item, or one of the built-in category icon modes:

- `AnyHide`
- `AnyHarvestPlant`
- `AnyFishAnimalMeat`
- `AnyFishMeat`

A custom requirement icon can also be loaded from a file path or embedded resource through `CustomTradeIcon`.

## Rewards

ETF currently supports gear item rewards and completion-only trades.

Gear rewards are defined with:

```csharp
Reward = CustomTradeReward.Gear("GEAR_Stick", 10)
```

Stackable reward behavior can be controlled with `CustomRewardStacking`:

| Mode | Behavior |
|---|---|
| `Auto` | ETF decides from the reward prefab. |
| `Stackable` | Force stackable behavior when the prefab supports it. |
| `NonStackable` | Treat the reward as separate item instances. |

Stackable item rewards are normalized when prepared in the Exchange Box, so multi-count rewards can behave as a single stack where possible.

## Completion-Only Trades

Completion-only trades are trades that complete normally but do not spawn a physical reward.

They are defined with:

```csharp
Reward = CustomTradeReward.None()
```

A completion-only trade must still define `DisplayReward`, because the Trader UI needs something visible to represent the exchange.

This is mostly useful when another mod wants to use the Trader as a cost/payment interface for an external unlock, buff, quest step, or progression system.

## Special Requests

ETF can also register custom special requests.

Special requests can override:

- The visible title.
- The visible description.
- The requirement text.
- Whether the requirement icon is hidden.
- The journal title.
- The journal description.
- Journal tips.

ETF also adds support for a secondary special request entry in the Trader journal.

Keep in mind that special requests touch more UI than normal trades.  
They should be tested carefully in the Trader panel, the Exchange Box, the journal, save/load, cancel flow, and completion flow.

## Save Data

ETF uses ModData to persist framework state.

It tracks:

- Completed non-repeatable trade IDs.
- Completion counts.
- Appearance roll results.
- Selected normal trade IDs.
- Active custom trade ID.
- Active trade multiplier.

This is needed so custom trades can survive save/load and still behave correctly when the player reloads during an active Trader exchange.

## Settings

Expanded Trading Framework adds a small ModSettings page.

| Setting | Behavior |
|---|---|
| Diagnostic logging | Enables detailed ETF state logging. This should stay disabled during normal play unless a modder asks for a reproduction log. |
| Emergency rescue hotkeys | Enables `Ctrl+Alt+T`, which clears the active Trader trade state as a recovery tool. Leave this disabled unless instructed to use it. |

The emergency hotkey is not a gameplay feature.  
It is only meant as a last-resort save recovery tool while testing Trader issues.

## Compatibility Notes

Expanded Trading Framework patches several Trader-related systems.

There may be overlap with other mods that modify:

- Sutherland's normal trade stock.
- Special request behavior.
- Trader trust behavior.
- Trader save/load behavior.
- The Exchange Box UI.
- The Trader selection panel.
- The Trader journal entries.

This does not mean these mods cannot work together.  
It only means these are the areas most likely to conflict if multiple mods patch the same vanilla Trader flow.

## For Developers

<details>
<summary><strong>Click to Expand</strong></summary>

### Registering a Basic Trade

Register your trades during your mod initialization.

```csharp
using ExpandedTradingFramework.Framework;
using Il2CppTLD.Trader;
using MelonLoader;

public class Core : MelonMod
{
    public override void OnInitializeMelon()
    {
        CustomTradeRegistry.RegisterExternalTrade(new CustomTradeDefinition
        {
            Id = "MyMod_CoalForStick",
            Enabled = true,
            Description = "My custom Trader exchange.",
            SelectionMode = CustomTradeSelectionMode.DrawPool,
            DrawPool = CustomTradeDrawPool.Common,
            MinTrust = 1,
            MaxPerTrade = 10,
            Repeatable = true,
            CostItems =
            [
                new CustomTradeItem
                {
                    ExchangeType = ExchangeItemType.GearAmount,
                    GearName = "GEAR_Coal",
                    Amount = 1
                }
            ],
            DisplayReward = CustomTradeDisplay.Gear("GEAR_Stick", 10),
            Reward = CustomTradeReward.Gear("GEAR_Stick", 10)
        });
    }
}
```

Use `RegisterExternalTrades(...)` when registering a list:

```csharp
CustomTradeRegistry.RegisterExternalTrades(TradeList.GetEnabledTrades());
```

Duplicate IDs are rejected.

### Category and Liquid Example

```csharp
new CustomTradeDefinition
{
    Id = "MyMod_WaterAndMeatForBait",
    Enabled = true,
    SelectionMode = CustomTradeSelectionMode.DrawPool,
    DrawPool = CustomTradeDrawPool.Common,
    MinTrust = 1,
    MaxPerTrade = 5,
    Repeatable = true,
    CostItems =
    [
        new CustomTradeItem
        {
            ExchangeType = ExchangeItemType.CategoryWeight,
            CategoryName = "Any Raw Meat",
            CategoryGearNames =
            [
                "GEAR_RawMeatRabbit",
                "GEAR_RawMeatDeer",
                "GEAR_RawMeatWolf"
            ],
            CategoryIcon = CategoryIcon.AnyFishAnimalMeat,
            WeightKilograms = 1.00f
        },
        new CustomTradeItem
        {
            ExchangeType = ExchangeItemType.Liquid,
            LiquidKind = CustomLiquidKind.PotableWater,
            LiquidLiters = 0.50f
        }
    ],
    DisplayReward = CustomTradeDisplay.Gear("GEAR_FishingBaitA", 5),
    Reward = CustomTradeReward.Gear("GEAR_FishingBaitA", 5)
};
```

### Completion-Only Example

```csharp
new CustomTradeDefinition
{
    Id = "MyMod_UnlockSomething",
    Enabled = true,
    SelectionMode = CustomTradeSelectionMode.AppearanceChance,
    AppearanceChance = 1.00f,
    MinTrust = 100,
    MaxPerTrade = 1,
    Repeatable = false,
    CostItems =
    [
        new CustomTradeItem
        {
            ExchangeType = ExchangeItemType.GearAmount,
            GearName = "GEAR_GoldNugget",
            Amount = 1
        }
    ],
    DisplayReward = CustomTradeDisplay.Gear("GEAR_EmergencyStim", 1),
    Reward = CustomTradeReward.None(),
    OnCompleted = context =>
    {
        // Apply your external unlock or side effect here.
    }
};
```

### Special Request Example

```csharp
new CustomTradeDefinition
{
    Id = "MyMod_SpecialRequest",
    Enabled = true,
    IsSpecialRequest = true,
    SelectionMode = CustomTradeSelectionMode.DrawPool,
    DrawPool = CustomTradeDrawPool.AlwaysShown,
    MinTrust = 250,
    MaxPerTrade = 1,
    Repeatable = false,
    SpecialRequest = new CustomSpecialRequestDisplay
    {
        Title = "A Strange Request",
        Description = "Sutherland has something unusual in mind.",
        RequirementText = "Bring the required supplies.",
        HideRequirementIcon = true,
        JournalTitle = "A Strange Request",
        JournalDescription = "Complete Sutherland's custom request.",
        JournalTips = ["Check the exchange box requirements."]
    },
    CostItems =
    [
        new CustomTradeItem
        {
            ExchangeType = ExchangeItemType.GearAmount,
            GearName = "GEAR_Charcoal",
            Amount = 10
        }
    ],
    DisplayReward = CustomTradeDisplay.Gear("GEAR_EmergencyStim", 1),
    Reward = CustomTradeReward.Gear("GEAR_EmergencyStim", 1)
};
```

</details>

## Installation

1. Install MelonLoader.
2. Install [ModSettings](https://github.com/DigitalzombieTLD/ModSettings/) and [ModData](https://github.com/dommrogers/ModData).
3. Place `ExpandedTradingFramework.dll` inside your `Mods` folder.

## AI Notice

This mod was partially developed with the assistance of AI tools.

AI support was used for structural guidance, debugging assistance and documentation refinement.

Coding, core design decisions, system architecture, balancing, and implementation logic remain fully human-driven.
