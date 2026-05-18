using NoteFlow.Studio.Model.Config;
using NoteFlow.Studio.Repository.Config;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;

namespace NoteFlow.Studio.Views
{
    public partial class BasicSettingsView : UserControl
    {
        private readonly ConfigRepository _configRepository;

        public BasicSettingsView()
        {
            InitializeComponent();
            _configRepository = new ConfigRepository();
            LoadConfig();
        }

        private void LoadConfig()
        {
            TxtVideoDownloadPath.Text = _configRepository.GetConfig(AppConfigKeys.VideoDownloadPath, "D:\\Downloads\\Videos");
            TxtToolDownloadPath.Text = _configRepository.GetConfig(AppConfigKeys.ToolDownloadPath, "D:\\Downloads\\Tools");
            TxtCliStartupPath.Text = _configRepository.GetConfig(AppConfigKeys.CliStartupPath, "D:\\Downloads\\");
            TxtObsidianRootPath.Text = _configRepository.GetConfig(AppConfigKeys.ObsidianRootPath, "D:\\Obsidian Note\\");
            TxtObsidianApi.Text = _configRepository.GetConfig("ObsidianApiUrl", "http://127.0.0.1:27123");
            TxtAnythingLlmApi.Text = _configRepository.GetConfig("AnythingLlmApi", "http://127.0.0.1:3001/api");
            TxtAnythingLlmKey.Password = _configRepository.GetConfig("AnythingLlmKey", "");
        }

        private async void BtnSave_Click(object sender, RoutedEventArgs e)
        {
            _configRepository.SetConfig(AppConfigKeys.VideoDownloadPath, TxtVideoDownloadPath.Text);
            _configRepository.SetConfig(AppConfigKeys.ToolDownloadPath, TxtToolDownloadPath.Text);
            _configRepository.SetConfig(AppConfigKeys.CliStartupPath, TxtCliStartupPath.Text);
            _configRepository.SetConfig(AppConfigKeys.ObsidianRootPath, TxtObsidianRootPath.Text);
            _configRepository.SetConfig("ObsidianApiUrl", TxtObsidianApi.Text);
            _configRepository.SetConfig("AnythingLlmApi", TxtAnythingLlmApi.Text);
            _configRepository.SetConfig("AnythingLlmKey", TxtAnythingLlmKey.Password);

            TxtMessage.Text = "保存成功!";
            TxtMessage.Visibility = Visibility.Visible;
            await Task.Delay(2000);
            TxtMessage.Visibility = Visibility.Collapsed;
        }
    }
}