using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices.WindowsRuntime;
using Windows.Foundation;
using Windows.Foundation.Collections;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Controls.Primitives;
using Windows.UI.Xaml.Data;
using Windows.UI.Xaml.Input;
using Windows.UI.Xaml.Media;
using Windows.UI.Xaml.Navigation;

using System.Threading;
using System.Threading.Tasks;


// The Blank Page item template is documented at http://go.microsoft.com/fwlink/?LinkId=402352&clcid=0x409

namespace WindowsIoTClient
{
    /// <summary>
    /// An empty page that can be used on its own or navigated to within a Frame.
    /// </summary>
    /// 

    public sealed partial class MainPage : Page
    {
        private SensorAccess Sensdata;
        private SensorAccess.SensorData RetrieveData;
        private int queryInterval = 2;  // query every 2s

        public MainPage()
        {
            Sensdata = new SensorAccess();
            InitializeComponent();
            //ShowData();
        }
        private void Receive_Click(object sender, RoutedEventArgs e)
        {
            StartMonitoring();
            Receive.IsEnabled = false;
        }

        private async void StartMonitoring()
        {

            while (true)
            {
                await ShowData();
                await System.Threading.Tasks.Task.Delay(TimeSpan.FromSeconds(queryInterval));//Thread.Sleep(queryInterval);
            }
        }

        private async Task<Boolean> ShowData()
        {
            RetrieveData = await Sensdata.ReceiveData();
            if (RetrieveData != null)
            {
                Accel_x_text.Text = RetrieveData.AccelerometerX.ToString();
                Accel_y_text.Text = RetrieveData.AccelerometerY.ToString();
                Accel_z_text.Text = RetrieveData.AccelerometerZ.ToString();
                State_text.Text = RetrieveData.State.ToString();
                Date_time_text.Text = RetrieveData.MeasurementTime.ToString();
            }
            return true;
        }
    }
}
