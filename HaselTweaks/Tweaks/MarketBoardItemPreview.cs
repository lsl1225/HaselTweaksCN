using Dalamud.Game.Addon.Lifecycle;
using Dalamud.Game.Addon.Lifecycle.AddonArgTypes;
using Dalamud.Plugin.Services;
using FFXIVClientStructs.FFXIV.Client.UI.Agent;
using FFXIVClientStructs.FFXIV.Component.GUI;
using HaselCommon.Services;
using HaselTweaks.Enums;
using HaselTweaks.Interfaces;
using Lumina.Excel.Sheets;
using Microsoft.Extensions.Logging;

namespace HaselTweaks.Tweaks;

[RegisterSingleton<ITweak>(Duplicate = DuplicateStrategy.Append)]
public sealed unsafe class MarketBoardItemPreview(
    ILogger<MarketBoardItemPreview> Logger,
    IAddonLifecycle AddonLifecycle,
    ExcelService ExcelService,
    TextService TextService,
    ItemService ItemService)
    : ITweak
{
    public string InternalName => nameof(MarketBoardItemPreview);
    public TweakStatus Status { get; set; } = TweakStatus.Uninitialized;

    public void OnInitialize() { }

    public void OnEnable()
    {
        AddonLifecycle.RegisterListener(AddonEvent.PostReceiveEvent, "ItemSearch", ItemSearch_PostReceiveEvent);
    }

    public void OnDisable()
    {
        AddonLifecycle.UnregisterListener(AddonEvent.PostReceiveEvent, "ItemSearch", ItemSearch_PostReceiveEvent);
    }

    void IDisposable.Dispose()
    {
        if (Status is TweakStatus.Disposed or TweakStatus.Outdated)
            return;

        OnDisable();

        Status = TweakStatus.Disposed;
        GC.SuppressFinalize(this);
    }

    private void ItemSearch_PostReceiveEvent(AddonEvent type, AddonArgs args)
    {
        if (args is not AddonReceiveEventArgs addonReceiveEventArgs || addonReceiveEventArgs.AtkEventType != (byte)AtkEventType.ListItemRollOver)
            return;

        var eventData = (AtkEventData*)addonReceiveEventArgs.Data;
        var itemIndex = eventData->ListItemData.SelectedIndex;
        var itemId = AgentItemSearch.Instance()->ListingPageItemIds[itemIndex];
        Logger.LogTrace("Previewing Index {atkEventData} with ItemId {itemId} @ {addr:X}", itemIndex, itemId, args.Addon + itemIndex * 4 + 0xBBC);

        if (!ExcelService.TryGetRow<Item>(itemId, out var item))
            return;

        if (!ItemService.CanTryOn(item))
        {
            Logger.LogInformation("Skipping preview of {name}, because it can't be tried on", TextService.GetItemName(item));
            return;
        }

        AgentTryon.TryOn(((AtkUnitBase*)args.Addon)->Id, itemId, 0, 0, 0);
    }
}
