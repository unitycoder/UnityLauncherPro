using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Navigation;
using System.Windows.Shapes;

namespace UnityLauncherPro
{
    /// <summary>
    /// Interaction logic for MainWindow.xaml
    /// </summary>
    public partial class MainWindow : Window
    {
        public MainWindow()
        {
            InitializeComponent();
            Start();
        }

        void Start()
        {
            // test data
            dataGrid.Items.Add(new TestData { Project = "asdf", Version = "5000", Path = "A:/", Modified = DateTime.Now, Arguments ="", GITBranch = "-" });
            dataGrid.Items.Add(new TestData { Project = "asdf asd", Version = "2", Path = "C:/", Modified = DateTime.Now, Arguments = "", GITBranch = "-" });
            dataGrid.Items.Add(new TestData { Project = "kuykkyu", Version = "23.23.23", Path = "8,1", Modified = DateTime.Now, Arguments = "", GITBranch = "-" });
            dataGrid.Items.Add(new TestData { Project = "RT435y", Version = "3333", Path = "X:/", Modified = DateTime.Now, Arguments = "", GITBranch = "-" });
        }
    }

    public struct TestData
    {
        public string Project { set; get; }
        public string Version { set; get; }
        public string Path { set; get; }
        public DateTime Modified { set; get; }
        public string Arguments { set; get; }
        public string GITBranch { set; get; }
    }

}
