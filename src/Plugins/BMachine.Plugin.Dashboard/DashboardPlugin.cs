using BMachine.SDK;
using BMachine.Plugin.Dashboard;

namespace BMachine.Plugin.Dashboard;

[PluginInfo("bmachine.dashboard", "Dashboard Plugin", Version = "1.0.0", Description = "Main Dashboard UI")]
public class DashboardPlugin : BasePlugin
{
    public override string Id => "bmachine.dashboard";
    public override string Name => "Dashboard";
    
    public override IEnumerable<IWidget> GetDashboardWidgets()
    {
        // Test reference
        var card = new BMachine.UI.Controls.ActionCard();
        
        return base.GetDashboardWidgets();
    }
}
