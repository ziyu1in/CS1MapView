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
                return $"{CS2MapView.Beta.ProjectInfo.Name} — Cities: Skylines 1\r\n{formAssembly?.Name} : {formAssembly?.Version}\r\n{coreAssembly?.Name} : {coreAssembly?.Version}\r\n\r\n{CS2MapView.Beta.ProjectInfo.DevelopmentDeclaration}\r\n\r\n{CS2MapView.Beta.ProjectInfo.UpstreamAcknowledgment}\r\n\r\nUpstream: gansaku/CS2MapView ecba18732777acb3de18a8378f06ab5047bc0e23 (MIT).";
            }
        }
    }
}
