using NoteFlow.Studio.Views.VideoGeneration;
using System;
using System.Windows;
using System.Windows.Controls;

namespace NoteFlow.Studio.Views
{
    public partial class VideoGenerationView : UserControl
    {
        public string CurrentOverviewId { get; set; } = Guid.NewGuid().ToString();

        public void LoadRecord(string overviewId)
        {
            CurrentOverviewId = overviewId;
            var overview = new NoteFlow.Studio.Repository.RemotionRepository().GetOverview(overviewId);
            CleanTabControl?.ResetForNewRemotion(CurrentOverviewId);
            ScriptTabControl?.ResetForNewRemotion(CurrentOverviewId);
            AnimationTabControl?.ResetForNewRemotion(CurrentOverviewId);
            BRollTabControl?.ResetForNewRemotion(CurrentOverviewId);

            if (CleanTabControl != null) 
            {
                CleanTabControl.CurrentOverviewId = CurrentOverviewId;
                if (overview != null)
                {
                    CleanTabControl.SourceTextPath = overview.SourceTextPath;
                    CleanTabControl.CleanedText = overview.CleanedText;
                }
            }
            if (ScriptTabControl != null) 
            {
                ScriptTabControl.CurrentOverviewId = CurrentOverviewId;
                if (overview != null)
                {
                    ScriptTabControl.ScriptCleanedText = overview.CleanedText;
                    ScriptTabControl.ScriptResultText = overview.StoryboardScript;
                }
            }
            if (AnimationTabControl != null) 
            {
                AnimationTabControl.CurrentOverviewId = CurrentOverviewId;
                if (overview != null)
                {
                    AnimationTabControl.ScriptResultText = overview.StoryboardScript;
                }
            }
        }

        private void CreateNewRemotion()
        {
            CurrentOverviewId = Guid.NewGuid().ToString();
            CleanTabControl?.ResetForNewRemotion(CurrentOverviewId);
            ScriptTabControl?.ResetForNewRemotion(CurrentOverviewId);
            AnimationTabControl?.ResetForNewRemotion(CurrentOverviewId);
            BRollTabControl?.ResetForNewRemotion(CurrentOverviewId);

            TabClean.IsChecked = true;
            TabButton_Click(TabClean, null);
        }

        public VideoGenerationView()
        {
            InitializeComponent();

            // 将唯一的 OverviewId 分发给三个子 Tab，保证后续能够共享
            if (CleanTabControl != null) CleanTabControl.CurrentOverviewId = CurrentOverviewId;
            if (ScriptTabControl != null) ScriptTabControl.CurrentOverviewId = CurrentOverviewId;
            if (AnimationTabControl != null) AnimationTabControl.CurrentOverviewId = CurrentOverviewId;
            if (BRollTabControl != null) BRollTabControl.CurrentOverviewId = CurrentOverviewId;

        }

        private void GoToScriptTab()
        {
            TabScript.IsChecked = true;
            TabButton_Click(TabScript, null);
        }

        private void GoToAnimationTab()
        {
            TabAnimation.IsChecked = true;
            // 传递分镜脚本结果与关联ID到动画页面 (关联 ID 已由初始化时分配)
            if (AnimationTabControl != null && ScriptTabControl != null)
            {
                AnimationTabControl.ScriptResultText = ScriptTabControl.ScriptResultText;
            }
            TabButton_Click(TabAnimation, null);
        }

        private void TabButton_Click(object sender, RoutedEventArgs e)
        {
            CleanTabControl.Visibility = Visibility.Collapsed;
            ScriptTabControl.Visibility = Visibility.Collapsed;
            if (AnimationTabControl != null) AnimationTabControl.Visibility = Visibility.Collapsed;
            if (BRollTabControl != null) BRollTabControl.Visibility = Visibility.Collapsed;

            if (sender == TabClean)
            {
                CleanTabControl.Visibility = Visibility.Visible;
            }
            else if (sender == TabScript)
            {
                if (string.IsNullOrEmpty(ScriptTabControl.ScriptCleanedText) && !string.IsNullOrEmpty(CleanTabControl.CleanedText))
                {
                    ScriptTabControl.ScriptCleanedText = CleanTabControl.CleanedText;
                }
                ScriptTabControl.Visibility = Visibility.Visible;
            }
            else if (sender == TabAnimation)
            {
                if (AnimationTabControl != null)
                {
                    AnimationTabControl.Visibility = Visibility.Visible;
                }
            }
            else if (sender == TabBRoll)
            {
                if (BRollTabControl != null)
                {
                    BRollTabControl.CurrentOverviewId = CurrentOverviewId;
                    BRollTabControl.LoadStoryboards();
                    BRollTabControl.Visibility = Visibility.Visible;
                }
            }
        }

    }
}
