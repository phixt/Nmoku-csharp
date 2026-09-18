using Godot;

public partial class Main : Node
{
    public override void _Ready()
    {
        int custom = 0;
        foreach (string action in InputMap.GetActions())
        {
            if (!action.StartsWith("ui_"))
                custom++;
        }
        GD.Print($"Nmoku M0: engine boot OK (custom input actions={custom}, audio buses={AudioServer.BusCount})");
    }
}
