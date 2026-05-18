using NoteFlow.Studio.Model.Config;
using NoteFlow.Studio.Repository.Config;
using NoteFlow.Studio.Infrastructure.Logging;
using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using RestSharp;
using System.Text.Json;

namespace NoteFlow.Studio.Views
{
    public partial class LLMSettingsView : UserControl
    {
        private readonly AiModelRepository _aiModelRepository;
        public ObservableCollection<AiModel> Models { get; set; }

        public LLMSettingsView()
        {
            InitializeComponent();
            _aiModelRepository = new AiModelRepository();
            Models = new ObservableCollection<AiModel>();
            ProviderListBox.ItemsSource = Models;
            LoadModels();
        }

        private void LoadModels()
        {
            Models.Clear();
            var models = _aiModelRepository.GetAll();
            var defaultNames = new[] { "OpenAI", "DeepSeek", "Qwen", "Claude", "Gemini", "Ollama" };
            bool added = false;

            foreach (var dn in defaultNames)
            {
                if (!models.Any(m => m.Name != null && m.Name.Equals(dn, StringComparison.OrdinalIgnoreCase)))
                {
                    string endpoint = "";
                    if (dn == "Ollama") endpoint = "http://127.0.0.1:11434";
                    
                    models.Add(new AiModel 
                    { 
                        Id = Guid.NewGuid().ToString("N"), 
                        Name = dn, 
                        IsCustom = false, 
                        Endpoint = endpoint, 
                        IsEnabled = (dn == "Ollama"),
                        ModelName = dn == "Ollama" ? "" : dn // Default ModelName
                    });
                    added = true;
                }
            }

            if (added)
            {
                _aiModelRepository.SaveAll(models);
            }
            
            foreach (var model in models)
            {
                Models.Add(model);
            }
        }

        private void SaveAllModels()
        {
            _aiModelRepository.SaveAll(Models);
            var mainWindow = Window.GetWindow(this) as MainWindow;
            mainWindow?.LoadGlobalAiModels();
        }

        private void ProviderListBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (ProviderListBox.SelectedItem is AiModel selected)
            {
                ConfigPanel.Visibility = Visibility.Visible;
                TxtProviderTitle.Text = $"{selected.Name} 设置";
                TxtProviderName.Text = selected.Name;
                
                TxtEndpoint.Text = selected.Endpoint;
                TxtApiKey.Text = selected.ApiKey;
                TxtModelName.Text = selected.ModelName;

                // 默认不可删除
                var defaultNames = new[] { "OpenAI", "DeepSeek", "Qwen", "Claude", "Gemini", "Ollama" };
                bool isDefault = defaultNames.Contains(selected.Name, StringComparer.OrdinalIgnoreCase);
                BtnDeleteCustom.Visibility = isDefault ? Visibility.Collapsed : Visibility.Visible;

                if (selected.Name != null && selected.Name.Contains("Ollama", StringComparison.OrdinalIgnoreCase))
                {
                    PanelModelName.Visibility = Visibility.Collapsed;
                    PanelOllamaConfig.Visibility = Visibility.Visible;
                    
                    if (!string.IsNullOrEmpty(selected.ModelName) && !CmbOllamaModels.Items.Contains(selected.ModelName))
                    {
                        CmbOllamaModels.Items.Add(selected.ModelName);
                    }
                    CmbOllamaModels.SelectedItem = selected.ModelName;
                }
                else
                {
                    PanelModelName.Visibility = Visibility.Visible;
                    PanelOllamaConfig.Visibility = Visibility.Collapsed;
                }
            }
            else
            {
                ConfigPanel.Visibility = Visibility.Hidden;
            }
        }

        private async void BtnTestOllama_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var endpoint = TxtEndpoint.Text.TrimEnd('/');
                if (string.IsNullOrEmpty(endpoint)) endpoint = "http://127.0.0.1:11434";

                var client = new RestClient(endpoint);
                var request = new RestRequest("/api/tags", Method.Get);
                var response = await client.ExecuteAsync(request);

                if (response.IsSuccessful && !string.IsNullOrEmpty(response.Content))
                {
                    var result = JsonSerializer.Deserialize<JsonElement>(response.Content);
                    if (result.TryGetProperty("models", out var modelsElement))
                    {
                        CmbOllamaModels.Items.Clear();
                        foreach (var model in modelsElement.EnumerateArray())
                        {
                            if (model.TryGetProperty("name", out var nameElement))
                            {
                                CmbOllamaModels.Items.Add(nameElement.GetString());
                            }
                        }
                        
                        if (CmbOllamaModels.Items.Count > 0)
                        {
                            CmbOllamaModels.SelectedIndex = 0;
                            MessageBox.Show("Ollama 连通性测试成功！已加载模型列表。", "成功", MessageBoxButton.OK, MessageBoxImage.Information);
                        }
                        else
                        {
                            MessageBox.Show("连通成功，但未找到任何模型，请先在Ollama拉取模型。", "提示", MessageBoxButton.OK, MessageBoxImage.Warning);
                        }
                    }
                }
                else
                {
                    MessageBox.Show($"Ollama 连通失败：\n{response.ErrorMessage ?? response.StatusCode.ToString()}", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
            catch (Exception ex)
            {
                AppLogger.Error($"Ollama 连通发生异常：\n{ex.Message}", ex);
                MessageBox.Show($"Ollama 连通发生异常：\n{ex.Message}", "异常", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void BtnAddCustom_Click(object sender, RoutedEventArgs e)
        {
            var newModel = new AiModel
            {
                Id = Guid.NewGuid().ToString("N"),
                Name = "自定义模型",
                IsCustom = true,
                IsEnabled = true,
                Endpoint = "http://localhost:11434",
                ModelName = "my-model"
            };
            Models.Add(newModel);
            SaveAllModels();
            ProviderListBox.SelectedItem = newModel;
        }

        private void BtnDeleteCustom_Click(object sender, RoutedEventArgs e)
        {
            if (ProviderListBox.SelectedItem is AiModel selected)
            {
                if (MessageBox.Show($"确定要删除模型 '{selected.Name}' 吗？", "确认删除", MessageBoxButton.YesNo, MessageBoxImage.Question) == MessageBoxResult.Yes)
                {
                    Models.Remove(selected);
                    SaveAllModels();
                }
            }
        }

        private async void BtnTestConnection_Click(object sender, RoutedEventArgs e)
        {
            if (ProviderListBox.SelectedItem is AiModel selected)
            {
                TxtMessage.Foreground = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Colors.Orange);
                TxtMessage.Text = "正在测试连接...";
                TxtMessage.Visibility = Visibility.Visible;
                BtnTestConnection.IsEnabled = false;

                try
                {
                    var testModel = new AiModel
                    {
                        Name = TxtProviderName.Text,
                        Endpoint = TxtEndpoint.Text,
                        ApiKey = TxtApiKey.Text,
                        ModelName = string.IsNullOrEmpty(TxtModelName.Text) ? "dummy" : TxtModelName.Text
                    };

                    if (testModel.Name != null && testModel.Name.Contains("Ollama", StringComparison.OrdinalIgnoreCase))
                    {
                        testModel.ModelName = CmbOllamaModels.SelectedItem?.ToString() ?? "llama2";
                    }

                    var llmService = new NoteFlow.Studio.Service.Providers.LLMService();
                    bool success = await llmService.TestConnectionAsync(testModel);

                    if (success)
                    {
                        TxtMessage.Foreground = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Colors.Green);
                        TxtMessage.Text = "连接成功！";
                    }
                    else
                    {
                        TxtMessage.Foreground = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Colors.Red);
                        TxtMessage.Text = "连接失败，请检查配置。";
                    }
                }
                catch (Exception ex)
                {
                    AppLogger.Error($"连接异常: {ex.Message}", ex);
                    TxtMessage.Foreground = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Colors.Red);
                    TxtMessage.Text = $"连接异常: {ex.Message}";
                }
                finally
                {
                    BtnTestConnection.IsEnabled = true;
                    await Task.Delay(3000);
                    if (TxtMessage.Text.StartsWith("连接"))
                        TxtMessage.Visibility = Visibility.Collapsed;
                }
            }
        }

        private async void BtnSaveProvider_Click(object sender, RoutedEventArgs e)
        {
            if (ProviderListBox.SelectedItem is AiModel selected)
            {
                selected.Name = TxtProviderName.Text;
                selected.Endpoint = TxtEndpoint.Text;
                selected.ApiKey = TxtApiKey.Text;

                if (selected.Name != null && selected.Name.Contains("Ollama", StringComparison.OrdinalIgnoreCase))
                {
                    selected.ModelName = CmbOllamaModels.SelectedItem?.ToString() ?? "";
                }
                else
                {
                    selected.ModelName = TxtModelName.Text;
                }
                
                ProviderListBox.Items.Refresh();
                SaveAllModels();

                TxtMessage.Text = "配置已保存";
                TxtMessage.Visibility = Visibility.Visible;
                await Task.Delay(2000);
                TxtMessage.Visibility = Visibility.Collapsed;
            }
        }
        
        private void ProviderToggle_Changed(object sender, RoutedEventArgs e)
        {
            if (sender is ToggleButton toggle && toggle.DataContext is AiModel model)
            {
                SaveAllModels();
            }
        }
    }
}

