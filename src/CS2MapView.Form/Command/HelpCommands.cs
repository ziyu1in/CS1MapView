using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Reflection;

namespace CS2MapView.Form.Command
{
    internal class HelpCommands
    {
        private MainForm MainForm { get; init; }

        internal HelpCommands(MainForm mainForm)
        {
            MainForm = mainForm;
        }
        internal void ShowVersionInfo()
        {
            MessageBox.Show(VersionInfoText, CS2MapView.Beta.ProjectInfo.Name, MessageBoxButtons.OK);
        }
        internal static string VersionInfoText
        {
            get
            {
                var coreAssembly = Assembly.GetAssembly(typeof(ICS2MapViewRoot))?.GetName();
                var formAssembly = Assembly.GetAssembly(typeof(MainForm))?.GetName();
                return UiText.Format("About_VersionInfo", CS2MapView.Beta.ProjectInfo.Name,
                    formAssembly?.Name, formAssembly?.Version, coreAssembly?.Name, coreAssembly?.Version,
                    UiText.Get("About_DevelopmentDeclaration"), UiText.Get("About_UpstreamAcknowledgment"),
                    UiText.Get("About_UpstreamRevision"));
            }
        }
    }
}
