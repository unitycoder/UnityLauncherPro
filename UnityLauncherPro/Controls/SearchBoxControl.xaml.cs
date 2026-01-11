using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace UnityLauncherPro.Controls
{
    public partial class SearchBoxControl : UserControl
    {
        public event TextChangedEventHandler SearchTextChanged;
        public event RoutedEventHandler SearchCleared;

        public event KeyEventHandler SearchKeyDown;

        public SearchBoxControl()
        {
            InitializeComponent();
        }

        public string SearchText
        {
            get { return txtSearchBox.Text; }
            set { txtSearchBox.Text = value; }
        }

        public new void Focus()
        {
            txtSearchBox.Focus();
            txtSearchBox.Select(txtSearchBox.Text.Length, 0);
        }

        public void Clear()
        {
            txtSearchBox.Text = "";
        }

        private void TxtSearchBox_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            SearchKeyDown?.Invoke(this, e);
        }

        private void TxtSearchBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            SearchTextChanged?.Invoke(this, e);
        }

        private void OnClearSearchClick(object sender, RoutedEventArgs e)
        {
            Clear();
            SearchCleared?.Invoke(this, e);
        }
    }
}
