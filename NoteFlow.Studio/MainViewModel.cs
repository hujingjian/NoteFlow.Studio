using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Windows.Input;
using NoteFlow.Studio.Model;
using NoteFlow.Studio.Repository;

namespace NoteFlow.Studio
{
    public class MainViewModel : INotifyPropertyChanged
    {
        private readonly ProcessingRecordRepository _repository;
        public ObservableCollection<ProcessingRecord> ProcessingRecords { get; set; }

        public ICommand OpenVideoUrlCommand { get; }

        public MainViewModel()
        {
            _repository = new ProcessingRecordRepository();
            ProcessingRecords = new ObservableCollection<ProcessingRecord>();
            OpenVideoUrlCommand = new RelayCommand<string>(OpenVideoUrl);
            LoadProcessingRecords();
        }

        public void LoadProcessingRecords()
        {
            var records = _repository.GetAll();
            ProcessingRecords.Clear();
            foreach (var record in records)
            {
                ProcessingRecords.Add(record);
            }
        }

        private void OpenVideoUrl(string url)
        {
            if (App.Current.MainWindow is MainWindow mainWindow && !string.IsNullOrEmpty(url))
            {
                mainWindow.NavigateHomeToUrl(url);
            }
        }

        public event PropertyChangedEventHandler PropertyChanged;
        protected virtual void OnPropertyChanged(string propertyName)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }

    public class RelayCommand<T> : ICommand
    {
        private readonly Action<T> _execute;
        private readonly Predicate<T> _canExecute;

        public RelayCommand(Action<T> execute, Predicate<T> canExecute = null)
        {
            _execute = execute;
            _canExecute = canExecute;
        }

        public bool CanExecute(object parameter) => _canExecute == null || _canExecute((T)parameter);

        public void Execute(object parameter) => _execute((T)parameter);

        public event EventHandler CanExecuteChanged
        {
            add { CommandManager.RequerySuggested += value; }
            remove { CommandManager.RequerySuggested -= value; }
        }
    }
}
