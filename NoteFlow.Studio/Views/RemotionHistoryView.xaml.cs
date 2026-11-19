using NoteFlow.Studio.Model;
using NoteFlow.Studio.Repository;
using System;
using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace NoteFlow.Studio.Views
{
    public partial class RemotionHistoryView : UserControl
    {
        private RemotionRepository _repository;
        private int _currentPage = 1;
        private int _pageSize = 10;
        private int _totalCount = 0;

        public event Action<string> OnViewRecordRequested;

        public ObservableCollection<RemotionOverview> HistoryRecords { get; set; }

        public RemotionHistoryView()
        {
            InitializeComponent();
            _repository = new RemotionRepository();
            HistoryRecords = new ObservableCollection<RemotionOverview>();
            HistoryListView.ItemsSource = HistoryRecords;
        }

        public void LoadData()
        {
            _currentPage = 1;
            RefreshList();
        }

        private void RefreshList()
        {
            _totalCount = _repository.GetRemotionOverviewCount();
            int totalPages = (int)Math.Ceiling(_totalCount / (double)_pageSize);
            if (totalPages == 0) totalPages = 1;
            if (_currentPage > totalPages) _currentPage = totalPages;

            var list = _repository.GetPagedRemotionOverviews(_currentPage, _pageSize);
            HistoryRecords.Clear();
            foreach (var item in list)
            {
                HistoryRecords.Add(item);
            }

            TxtPageInfo.Text = $"第 {_currentPage} 页 / 共 {totalPages} 页";
            BtnPrevPage.IsEnabled = _currentPage > 1;
            BtnNextPage.IsEnabled = _currentPage < totalPages;
        }

        private void BtnPrevPage_Click(object sender, RoutedEventArgs e)
        {
            if (_currentPage > 1)
            {
                _currentPage--;
                RefreshList();
            }
        }

        private void BtnNextPage_Click(object sender, RoutedEventArgs e)
        {
            int totalPages = (int)Math.Ceiling(_totalCount / (double)_pageSize);
            if (_currentPage < totalPages)
            {
                _currentPage++;
                RefreshList();
            }
        }

        private void BtnView_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn && btn.CommandParameter is RemotionOverview record)
            {
                OnViewRecordRequested?.Invoke(record.Id);
            }
        }

        private void HistoryListView_MouseDoubleClick(object sender, MouseButtonEventArgs e)
        {
            if (HistoryListView.SelectedItem is RemotionOverview record)
            {
                OnViewRecordRequested?.Invoke(record.Id);
            }
        }
    }
}