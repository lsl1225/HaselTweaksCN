using Dalamud.Hooking;
using Dalamud.Plugin.Services;
using FFXIVClientStructs.FFXIV.Client.Game.Character;
using FFXIVClientStructs.FFXIV.Client.Game.Control;
using FFXIVClientStructs.FFXIV.Client.Game.Object;
using FFXIVClientStructs.FFXIV.Client.UI.Misc;
using HaselCommon.Services;
using HaselTweaks.Config;
using HaselTweaks.Enums;
using HaselTweaks.Interfaces;
using HaselTweaks.Structs;
using HaselTweaks.Structs.Agents;
using HaselTweaks.Utils;
using Lumina.Excel.Sheets;
using Lumina.Text;
using Lumina.Text.Payloads;
using Lumina.Text.ReadOnly;

namespace HaselTweaks.Tweaks;

[RegisterSingleton<ITweak>(Duplicate = DuplicateStrategy.Append), AutoConstruct]
public unsafe partial class EnhancedTargetInfo : IConfigurableTweak
{
    private readonly IGameInteropProvider _gameInteropProvider;
    private readonly PluginConfig _pluginConfig;
    private readonly ConfigGui _configGui;
    private readonly IClientState _clientState;
    private readonly TextService _textService;
    private readonly ExcelService _excelService;
    private readonly SeStringEvaluatorService _seStringEvaluator;

    private Hook<HaselAgentHUD.Delegates.UpdateTargetInfo> _updateTargetInfoHook;
    private Hook<HaselRaptureTextModule.Delegates.FormatAddonText2IntIntUInt> _formatAddonText2IntIntUIntHook;

    private ReadOnlySeString _rewrittenHealthPercentageText;

    public string InternalName => nameof(EnhancedTargetInfo);
    public TweakStatus Status { get; set; } = TweakStatus.Uninitialized;

    public void OnInitialize()
    {
        _updateTargetInfoHook = _gameInteropProvider.HookFromAddress<HaselAgentHUD.Delegates.UpdateTargetInfo>(
            HaselAgentHUD.MemberFunctionPointers.UpdateTargetInfo,
            UpdateTargetInfoDetour);

        _formatAddonText2IntIntUIntHook = _gameInteropProvider.HookFromAddress<HaselRaptureTextModule.Delegates.FormatAddonText2IntIntUInt>(
            HaselRaptureTextModule.MemberFunctionPointers.FormatAddonText2IntIntUInt,
            FormatAddonText2IntIntUIntDetour);

        if (_excelService.TryGetRow<Addon>(2057, _clientState.ClientLanguage, out var row))
        {
            var builder = new SeStringBuilder();

            foreach (var payload in row.Text)
            {
                // Replace "<if([lnum1>0],<digit(lnum1,2)>,<num(lnum1)>)>" with just "<num(lnum1)>"
                if (payload.Type == ReadOnlySePayloadType.Macro && payload.MacroCode == MacroCode.If)
                {
                    builder.BeginMacro(MacroCode.Num).AppendLocalNumberExpression(1).EndMacro();
                    continue;
                }

                builder.Append(payload);
            }

            _rewrittenHealthPercentageText = builder.ToReadOnlySeString();
        }
    }

    public void OnEnable()
    {
        _updateTargetInfoHook.Enable();
        _formatAddonText2IntIntUIntHook.Enable();
    }

    public void OnDisable()
    {
        _updateTargetInfoHook.Disable();
        _formatAddonText2IntIntUIntHook.Disable();
    }

    void IDisposable.Dispose()
    {
        if (Status is TweakStatus.Disposed or TweakStatus.Outdated)
            return;

        OnDisable();
        _updateTargetInfoHook.Dispose();
        _formatAddonText2IntIntUIntHook.Dispose();

        Status = TweakStatus.Disposed;
    }

    private void UpdateTargetInfoDetour(HaselAgentHUD* thisPtr)
    {
        _updateTargetInfoHook.Original(thisPtr);
        UpdateTargetInfoStatuses();
    }

    private void UpdateTargetInfoStatuses()
    {
        var target = TargetSystem.Instance()->GetTargetObject();
        if (target == null || target->GetObjectKind() != ObjectKind.Pc)
            return;

        var chara = (BattleChara*)target;

        if (Config.DisplayMountStatus && chara->Mount.MountId != 0)
        {
            TargetStatusUtils.AddPermanentStatus(0, 216201, 0, 0, default, _textService.GetMountName(chara->Mount.MountId));
        }
        else if (Config.DisplayOrnamentStatus && chara->OrnamentData.OrnamentId != 0)
        {
            TargetStatusUtils.AddPermanentStatus(0, 216234, 0, 0, default, _textService.GetOrnamentName(chara->OrnamentData.OrnamentId));
        }
    }

    private byte* FormatAddonText2IntIntUIntDetour(HaselRaptureTextModule* self, uint addonRowId, int value1, int value2, uint value3)
    {
        if (addonRowId == 2057 && Config.RemoveLeadingZeroInHPPercentage)
        {
            var str = ((RaptureTextModule*)self)->UnkStrings1.GetPointer(1);
            str->SetString(_seStringEvaluator.Evaluate(_rewrittenHealthPercentageText, [value1, value2, value3], _clientState.ClientLanguage));
            return str->StringPtr;
        }

        return _formatAddonText2IntIntUIntHook!.Original(self, addonRowId, value1, value2, value3);
    }
}
