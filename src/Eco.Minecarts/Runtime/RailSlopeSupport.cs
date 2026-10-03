using Eco.Core.Plugins.Interfaces;
using Eco.Gameplay.Blocks;
using Eco.Minecarts.Track;
using Eco.Shared.Math;
using Eco.Gameplay.Items;
using Eco.World.Blocks;
using System.Reflection;

namespace Eco.Minecarts.Runtime;

/// <summary>Native world events enforce support without a client loader.</summary>
public sealed class RailSlopeSupport : IModInit
{
    private static int subscribed;
    public static void PostInitialize()
    {
        if (Interlocked.Exchange(ref subscribed, 1) == 0)
            Eco.World.World.OnBlockChanged.Add(OnChanged);
    }

    private static void OnChanged(WrappedWorldPosition3i position)
    {
        var cell = new Vector3i(position.X, position.Y, position.Z);
        Validate(cell);
        // The changed block may be the support beneath an existing rail.
        if (cell.Y < Eco.World.World.VoxelSize.Y - 1)
            Validate(new Vector3i(cell.X, cell.Y + 1, cell.Z));
    }

    public static bool HasSupport(Type railType, Type supportType)
    {
        if (!VoxelTrackProfile.TryParse(railType.Name, out var rail) || !IsSlope(rail)) return true;
        var overlay = OverlayVersion(railType, supportType);
        if (overlay == null || !VoxelTrackProfile.TryParse(overlay.Name, out var matched)) return false;
        return rail.QuarterTurns==matched.QuarterTurns && rail.Shape.Replace("Slope","RampTop",StringComparison.Ordinal)==matched.Shape;
    }

    private static bool IsSlope(VoxelTrackProfile rail) => rail.Shape.StartsWith("Slope", StringComparison.Ordinal)
        || rail.Shape.StartsWith("RampTop", StringComparison.Ordinal);

    public static Type? NormalVersion(Type type)
    {
        if (!VoxelTrackProfile.TryParse(type.Name, out var rail) || !IsSlope(rail)) return null;
        var prefix = rail.Wooden ? "WoodenTrack" : rail.Chain ? "MinecartChain" : rail.Tram ? "TramTrack" : "MinecartTrack";
        var rotation = rail.QuarterTurns == 0 ? "" : (rail.QuarterTurns * 90).ToString();
        var phase=rail.Shape=="RampTop" ? "1" : rail.Shape[(rail.Shape.StartsWith("RampTop",StringComparison.Ordinal) ? 7 : 5)..];
        return type.Assembly.GetType("Eco.Mods.TechTree." + prefix + "Slope" + phase + rotation + "Block");
    }

    public static Type? OverlayVersion(Type railType, Type supportType)
    {
        if (!VoxelTrackProfile.TryParse(railType.Name,out var rail) || !IsSlope(rail)
            || VoxelTrackProfile.TryParse(supportType.Name,out _)) return null;
        var form=BlockFormManager.GetFormForBlock(supportType);
        var phase=form?.FormType.Name switch { "RampA"=>"1", "RampB"=>"2", "RampC"=>"3", "RampD"=>"4", "Ramp"=>"", _=>null };
        var index=form==null ? -1 : Array.IndexOf(form.BlockTypes,supportType);
        var turns=(index+3)%4;
        if(phase==null || index<0 || index>3)
        {
            // Road-tool ramps (notably dirt) are RampItem multi-blocks and do
            // not appear in the hammer's BlockFormManager. Use their real layout.
            var ramp=supportType.GetCustomAttribute<Ramp>();
            if(ramp==null || Item.Get(ramp.RampType) is not RampItem item) return null;
            var row=item.BlockTypes.FirstOrDefault(p=>p.Value.Contains(supportType));
            index=row.Value==null ? -1 : Array.IndexOf(row.Value,supportType);
            if(index<0 || index>3 || row.Value!.Length!=4) return null;
            turns=row.Key.X==1 ? 1 : row.Key.X==-1 ? 3 : row.Key.Z==-1 ? 2 : row.Key.Z==1 ? 0 : -1;
            if(turns<0) return null;
            phase=(index+1).ToString();
        }
        var prefix=rail.Wooden ? "WoodenTrack" : rail.Chain ? "MinecartChain" : rail.Tram ? "TramTrack" : "MinecartTrack";
        return railType.Assembly.GetType("Eco.Mods.TechTree."+prefix+"RampTop"+phase+(turns==0 ? "" : (turns*90).ToString())+"Block");
    }

    public static Type PlacementVersion(Type railType, Type supportType) =>
        OverlayVersion(railType,supportType) ?? NormalVersion(railType) ?? railType;

    private static void Validate(Vector3i cell)
    {
        var block = Eco.World.World.GetBlock(cell);
        if (block == null || NormalVersion(block.GetType()) == null) return;
        var support = cell.Y > 0 ? Eco.World.World.GetBlock(new Vector3i(cell.X, cell.Y - 1, cell.Z)) : null;
        var resolved=support==null ? NormalVersion(block.GetType())! : PlacementVersion(block.GetType(),support.GetType());
        // Overlay blocks stay registered for saves and visuals, but the hammer
        // offers only regular slopes. Terrain supplies phase and uphill rotation.
        if(resolved!=block.GetType()) Eco.World.World.SetBlock(resolved,cell);
    }
}
