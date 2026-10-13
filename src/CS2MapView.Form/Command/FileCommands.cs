using CS2MapView.Import;

namespace CS2MapView.Form.Command
{
    internal class FileCommands
    {
        private MainForm MainForm { get; init; }

        internal FileCommands(MainForm mainForm)
        {
            MainForm = mainForm;
        }

        internal async Task OpenFileAsync()
        {
            var ofd = new OpenFileDialog
            {
                CheckFileExists = true,
                Filter = "Cities: Skylines 1 map (*.cslmap, *.cslmap.gz)|*.cslmap;*.cslmap.gz"
            };
            if( MainForm.Context.UserSettings.OpenFileDir is not null)
            {
                if (Directory.Exists(MainForm.Context.UserSettings.OpenFileDir))
                {
                    ofd.InitialDirectory = MainForm.Context.UserSettings.OpenFileDir;
                }
            }
            if (ofd.ShowDialog() == DialogResult.OK)
            {
                await MainForm.OpenMapAsync(ofd.FileName);
            }
        }
    }
}
