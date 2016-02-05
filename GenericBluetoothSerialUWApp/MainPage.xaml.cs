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
using Windows.Devices.Enumeration;
using Windows.Devices.Bluetooth.Rfcomm;
using Windows.Networking.Sockets;
using System.Collections.ObjectModel;
using Windows.Storage.Streams;
using Windows.UI.Popups;
using System.Threading.Tasks;
using System.Threading;
using System.Text;
using Windows.System.Threading;

using Microsoft.WindowsAzure.Storage;
using Microsoft.WindowsAzure.Storage.Auth;
using Microsoft.WindowsAzure.Storage.Table;
using System.Net.Http;
using Windows.ApplicationModel.Background;




// The Blank Page item template is documented at http://go.microsoft.com/fwlink/?LinkId=402352&clcid=0x409

namespace GenericBluetoothSerialUWApp
{
    public class AccelerometerEntity : TableEntity
    {
        public AccelerometerEntity()
        {
            this.PartitionKey = "ADXL345";
            this.RowKey = Guid.NewGuid().ToString();
            MeasurementTime = DateTime.Now;
            AccelerometerX = 0.0;
            AccelerometerY = 0.0;
            AccelerometerZ = 0.0;
            State = null;
        }
        public DateTime MeasurementTime { get; set; }
        public double AccelerometerX { get; set; }
        public double AccelerometerY { get; set; }
        public double AccelerometerZ { get; set; }
        public string State { get; set; }
    }

    /// <summary>
    /// The Main Page for the app
    /// </summary>
    public sealed partial class MainPage : Page
    {
        string Title = "Generic Bluetooth Serial Universal Windows App";
        private Windows.Devices.Bluetooth.Rfcomm.RfcommDeviceService _service;
        private StreamSocket _socket;
        private DataWriter dataWriterObject;
        private DataReader dataReaderObject;
        ObservableCollection<PairedDeviceInfo> _pairedDevices;
        private CancellationTokenSource ReadCancellationTokenSource;

        //process and transmit timer
        public double tempAccelerometerX = 0;
        public double tempAccelerometerY = 0;
        public double tempAccelerometerZ = 0;

        private static ThreadPoolTimer timerDataProcess;
        private static ThreadPoolTimer timerDataTransfer;
        string strMessage;

        //for fall detection
        public double xtemp, ytemp, ztemp;
        public double a_norm;
        public int i = 0;
        static int BUFF_SIZE = 50;
        static public double[] window = new double[BUFF_SIZE];
        double sigma = 0.5, th = 10, th1 = 5, th2 = 2;
        public static String curr_state, prev_state;


        public MainPage()
        {
            this.InitializeComponent();
            MyTitle.Text = Title;
            InitializeRfcommDeviceService();
            for (int i = 0; i < BUFF_SIZE; i++)
            {
                window[i] = 0;
            }
            curr_state = null;
            prev_state = null;
        }



        async void InitializeRfcommDeviceService()
        {
            try
            {
                DeviceInformationCollection DeviceInfoCollection = await DeviceInformation.FindAllAsync(RfcommDeviceService.GetDeviceSelector(RfcommServiceId.SerialPort));


                var numDevices = DeviceInfoCollection.Count();

                // By clearing the backing data, we are effectively clearing the ListBox
                _pairedDevices = new ObservableCollection<PairedDeviceInfo>();
                _pairedDevices.Clear();

                if (numDevices == 0)
                {
                    //MessageDialog md = new MessageDialog("No paired devices found", "Title");
                    //await md.ShowAsync();
                    System.Diagnostics.Debug.WriteLine("InitializeRfcommDeviceService: No paired devices found.");
                }
                else
                {
                    // Found paired devices.
                    foreach (var deviceInfo in DeviceInfoCollection)
                    {
                        _pairedDevices.Add(new PairedDeviceInfo(deviceInfo));
                    }
                }
                PairedDevices.Source = _pairedDevices;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine("InitializeRfcommDeviceService: " + ex.Message);
            }
        }

        async private void ConnectDevice_Click(object sender, RoutedEventArgs e)
        {
            //Revision: No need to requery for Device Information as we alraedy have it:
            DeviceInformation DeviceInfo; // = await DeviceInformation.CreateFromIdAsync(this.TxtBlock_SelectedID.Text);
            PairedDeviceInfo pairedDevice = (PairedDeviceInfo)ConnectDevices.SelectedItem;
            DeviceInfo = pairedDevice.DeviceInfo;

            bool success = true;
            try
            {
                _service = await RfcommDeviceService.FromIdAsync(DeviceInfo.Id);

                if (_socket != null)
                {
                    // Disposing the socket with close it and release all resources associated with the socket
                    _socket.Dispose();
                }

                _socket = new StreamSocket();
                try
                {
                    // Note: If either parameter is null or empty, the call will throw an exception
                    await _socket.ConnectAsync(_service.ConnectionHostName, _service.ConnectionServiceName);
                }
                catch (Exception ex)
                {
                    success = false;
                    System.Diagnostics.Debug.WriteLine("Connect:" + ex.Message);
                }
                // If the connection was successful, the RemoteAddress field will be populated
                if (success)
                {
                    this.buttonDisconnect.IsEnabled = true;
                    this.buttonSend.IsEnabled = true;
                    this.buttonStartRecv.IsEnabled = true;
                    this.buttonStopRecv.IsEnabled = false;
                    string msg = String.Format("Connected to {0}!", _socket.Information.RemoteAddress.DisplayName);
                    //MessageDialog md = new MessageDialog(msg, Title);
                    System.Diagnostics.Debug.WriteLine(msg);
                    //await md.ShowAsync();
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine("Overall Connect: " + ex.Message);
                _socket.Dispose();
                _socket = null;
            }
        }






        private void ConnectDevices_DoubleTapped(object sender, DoubleTappedRoutedEventArgs e)
        {
            PairedDeviceInfo pairedDevice = (PairedDeviceInfo)ConnectDevices.SelectedItem;
            this.TxtBlock_SelectedID.Text = pairedDevice.ID;
            this.textBlockBTName.Text = pairedDevice.Name;
            ConnectDevice_Click(sender, e);
        }

        //Windows.Storage.Streams.Buffer InBuff;
        //Windows.Storage.Streams.Buffer OutBuff;
        //private StreamSocket _socket;
        private async void button_Click(object sender, RoutedEventArgs e)
        {
            //OutBuff = new Windows.Storage.Streams.Buffer(100);
            Button button = (Button)sender;
            if (button != null)
            {
                switch ((string)button.Content)
                {
                    case "Disconnect":
                        await this._socket.CancelIOAsync();
                        _socket.Dispose();
                        _socket = null;
                        this.textBlockBTName.Text = "";
                        this.TxtBlock_SelectedID.Text = "";
                        this.buttonDisconnect.IsEnabled = false;
                        this.buttonSend.IsEnabled = false;
                        this.buttonStartRecv.IsEnabled = false;
                        this.buttonStopRecv.IsEnabled = false;
                        this.ProcessButton.IsEnabled = false;
                        this.AzureButton.IsEnabled = false;
                        break;
                    case "Send":
                        //await _socket.OutputStream.WriteAsync(OutBuff);
                        Send(this.textBoxSendText.Text);
                        this.textBoxSendText.Text = "";
                        break;
                    case "Clear Send":
                        this.textBoxRecvdText.Text = "";
                        break;
                    case "Start Recv":
                        this.buttonStartRecv.IsEnabled = false;
                        this.buttonStopRecv.IsEnabled = true;
                        Listen();
                        break;
                    case "Stop Recv":
                        this.buttonStartRecv.IsEnabled = false;
                        this.buttonStopRecv.IsEnabled = false;
                        CancelReadTask();
                        break;
                    case "Refresh":
                        InitializeRfcommDeviceService();
                        break;
                }
            }
        }


        public async void Send(string msg)
        {
            try
            {
                if (_socket.OutputStream != null)
                {
                    // Create the DataWriter object and attach to OutputStream
                    dataWriterObject = new DataWriter(_socket.OutputStream);

                    //Launch the WriteAsync task to perform the write
                    await WriteAsync(msg);
                }
                else
                {
                    //status.Text = "Select a device and connect";
                }
            }
            catch (Exception ex)
            {
                //status.Text = "Send(): " + ex.Message;
                System.Diagnostics.Debug.WriteLine("Send(): " + ex.Message);
            }
            finally
            {
                // Cleanup once complete
                if (dataWriterObject != null)
                {
                    dataWriterObject.DetachStream();
                    dataWriterObject = null;
                }
            }
        }

        /// <summary>
        /// WriteAsync: Task that asynchronously writes data from the input text box 'sendText' to the OutputStream 
        /// </summary>
        /// <returns></returns>
        private async Task WriteAsync(string msg)
        {
            Task<UInt32> storeAsyncTask;

            if (msg == "")
                msg = "none";// sendText.Text;
            if (msg.Length != 0)
            //if (msg.sendText.Text.Length != 0)
            {
                // Load the text from the sendText input text box to the dataWriter object
                dataWriterObject.WriteString(msg);

                // Launch an async task to complete the write operation
                storeAsyncTask = dataWriterObject.StoreAsync().AsTask();

                UInt32 bytesWritten = await storeAsyncTask;
                if (bytesWritten > 0)
                {
                    string status_Text = msg + ", ";
                    status_Text += bytesWritten.ToString();
                    status_Text += " bytes written successfully!";
                    System.Diagnostics.Debug.WriteLine(status_Text);
                }
            }
            else
            {
                string status_Text2 = "Enter the text you want to write and then click on 'WRITE'";
                System.Diagnostics.Debug.WriteLine(status_Text2);
            }
        }



        /// <summary>
        /// - Create a DataReader object
        /// - Create an async task to read from the SerialDevice InputStream
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private async void Listen()
        {
            try
            {
                ReadCancellationTokenSource = new CancellationTokenSource();
                if (_socket.InputStream != null)
                {
                    dataReaderObject = new DataReader(_socket.InputStream);
                    this.buttonStopRecv.IsEnabled = true;
                    this.ProcessButton.IsEnabled = true;
                    this.AzureButton.IsEnabled = true;
                    this.buttonDisconnect.IsEnabled = false;
                    // keep reading the serial input
                    while (true)
                    {
                        await ReadAsync(ReadCancellationTokenSource.Token);
                    }
                }
            }
            catch (Exception ex)
            {
                this.buttonStopRecv.IsEnabled = false;
                this.buttonStartRecv.IsEnabled = false;
                this.buttonSend.IsEnabled = false;
                this.buttonDisconnect.IsEnabled = false;
                this.textBlockBTName.Text = "";
                this.TxtBlock_SelectedID.Text = "";
                if (ex.GetType().Name == "TaskCanceledException")
                {
                    System.Diagnostics.Debug.WriteLine("Listen: Reading task was cancelled, closing device and cleaning up");
                }
                else
                {
                    System.Diagnostics.Debug.WriteLine("Listen: " + ex.Message);
                }
            }
            finally
            {
                // Cleanup once complete
                if (dataReaderObject != null)
                {
                    dataReaderObject.DetachStream();
                    dataReaderObject = null;
                }
            }
        }

        /// <summary>
        /// ReadAsync: Task that waits on data and reads asynchronously from the serial device InputStream
        /// </summary>
        /// <param name="cancellationToken"></param>
        /// <returns></returns>
        private async Task ReadAsync(CancellationToken cancellationToken)
        {
            Task<UInt32> loadAsyncTask;

            uint ReadBufferLength = 1024;

            // If task cancellation was requested, comply
            cancellationToken.ThrowIfCancellationRequested();

            // Set InputStreamOptions to complete the asynchronous read operation when one or more bytes is available
            dataReaderObject.InputStreamOptions = InputStreamOptions.Partial;

            // Create a task object to wait for data on the serialPort.InputStream
            loadAsyncTask = dataReaderObject.LoadAsync(ReadBufferLength).AsTask(cancellationToken);

            // Launch the task and wait
            UInt32 bytesRead = await loadAsyncTask;
            if (bytesRead > 0)
            {
                try
                {
                    string recvdtxt = dataReaderObject.ReadString(bytesRead);
                    System.Diagnostics.Debug.WriteLine(recvdtxt);
                    this.textBoxRecvdText.Text += recvdtxt;
                    strMessage += recvdtxt;
                    //strMessage.Append(recvdtxt);
                    /*if (_Mode == Mode.JustConnected)
                    {
                        if (recvdtxt[0] == ArduinoLCDDisplay.keypad.BUTTON_SELECT_CHAR)
                        {
                            _Mode = Mode.Connected;

                            //Reset back to Cmd = Read sensor and First Sensor
                            await Globals.MP.UpdateText("@");
                            //LCD Display: Fist sensor and first comamnd
                            string lcdMsg = "~C" + Commands.Sensors[0];
                            lcdMsg += "~" + ArduinoLCDDisplay.LCD.CMD_DISPLAY_LINE_2_CH + Commands.CommandActions[1] + "           ";
                            Send(lcdMsg);

                            backButton_Click(null, null);
                        }
                    }
                    else if (_Mode == Mode.Connected)
                    {
                        await Globals.MP.UpdateText(recvdtxt);
                        recvdText.Text = "";
                        status.Text = "bytes read successfully!";
                    }*/
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine("ReadAsync: " + ex.Message);
                }

            }
        }

        /// <summary>
        /// CancelReadTask:
        /// - Uses the ReadCancellationTokenSource to cancel read operations
        /// </summary>
        private void CancelReadTask()
        {
            if (ReadCancellationTokenSource != null)
            {
                if (!ReadCancellationTokenSource.IsCancellationRequested)
                {
                    ReadCancellationTokenSource.Cancel();
                }
            }
        }


        /// <summary>
        ///  Class to hold all paired device information
        /// </summary>
        public class PairedDeviceInfo
        {
            internal PairedDeviceInfo(DeviceInformation deviceInfo)
            {
                this.DeviceInfo = deviceInfo;
                this.ID = this.DeviceInfo.Id;
                this.Name = this.DeviceInfo.Name;
            }

            public string Name { get; private set; }
            public string ID { get; private set; }
            public DeviceInformation DeviceInfo { get; private set; }
        }

        private void AzureButton_Click(object sender, RoutedEventArgs e)
        {
            timerDataTransfer = ThreadPoolTimer.CreatePeriodicTimer(dataTransferTick, TimeSpan.FromMilliseconds(Convert.ToInt32(3000)));
        }

        private async void dataTransferTick(ThreadPoolTimer timer)
        {
            try
            {
                CloudStorageAccount storageAccount = CloudStorageAccount.Parse("DefaultEndpointsProtocol=https;AccountName=myiotservicebusstorage;AccountKey=iNGhr9f+2pCUcVO/jrbokvwKhgIGjwNXLQzuwoqygW0gCTGlpJbPUvtmhI/g3epJ3sq3v0os5TS8KsGklEgmpA==");

                // Create the table client.
                CloudTableClient tableClient = storageAccount.CreateCloudTableClient();

                // Create the CloudTable object that represents the " AccelerometerTable " table.
                CloudTable table = tableClient.GetTableReference("AccelerometerTable");
                await table.CreateIfNotExistsAsync();

                // Create a new customer entity.
                AccelerometerEntity ent = new AccelerometerEntity();

                ent.MeasurementTime = DateTime.Now;
                ent.AccelerometerX = tempAccelerometerX;
                ent.AccelerometerY = tempAccelerometerY;
                ent.AccelerometerZ = tempAccelerometerZ;
                ent.State = curr_state;
                // Create the TableOperation that inserts the customer entity.
                TableOperation insertOperation = TableOperation.Insert(ent);
                // Execute the insert operation.
                await table.ExecuteAsync(insertOperation);
            }
            catch (Exception ex)
            {
                MessageDialog dialog = new MessageDialog("Error sending to Azure: " + ex.Message);
                dialog.ShowAsync();
            }
        }

        private void ProcessButton_Click(object sender, RoutedEventArgs e)
        {
            timerDataProcess = ThreadPoolTimer.CreatePeriodicTimer(dataProcessTick, TimeSpan.FromMilliseconds(Convert.ToInt32(300)));
        }

        private async void dataProcessTick(ThreadPoolTimer timer)
        {
            string[] arr = strMessage.Split(',');
            string process = null;
            int n = arr.Length;
            int i = 0;
            for (i = n - 1; i >= 0; i--)
            {
                if (arr[i].Length == 21)
                {
                    process = arr[i];
                    break;
                }
            }
            //string process = arr[0];
            double xtemp, ytemp, ztemp;
            if (process.Substring(2, 1) == "+")
                xtemp = Convert.ToDouble(process.Substring(3, 4));
            else
                xtemp = Convert.ToDouble(process.Substring(2, 5));

            if (process.Substring(9, 1) == "+")
                ytemp = Convert.ToDouble(process.Substring(10, 4));
            else
                ytemp = Convert.ToDouble(process.Substring(9, 5));

            if (process.Substring(16, 1) == "+")
                ztemp = Convert.ToDouble(process.Substring(17, 4));
            else
                ztemp = Convert.ToDouble(process.Substring(16, 5));

            AddData(xtemp, ytemp, ztemp);

            tempAccelerometerX = xtemp;
            tempAccelerometerY = ytemp;
            tempAccelerometerZ = ztemp;

            posture_recognition(window, ytemp);
            SystemState(curr_state, prev_state);

            if (prev_state!=curr_state)
            {
                prev_state = curr_state;
            }

            await textBoxRecvdText.Dispatcher.RunAsync(Windows.UI.Core.CoreDispatcherPriority.Normal, () =>
             {
                 textBoxRecvdText.Text = process;
             }
            );

            strMessage = "";
        }

        private void posture_recognition(double[] window2, double ay2)
        {
            // TODO Auto-generated method stub
            int zrc = compute_zrc(window2);
            if (zrc == 0)
            {
                if (Math.Abs(ay2) < th1)
                {
                    curr_state = "sitting";
                }
                else {
                    curr_state = "standing";
                }
            }
            else
            {
                if (zrc > th2)
                {
                    curr_state = "walking";
                }
                else {
                    curr_state = "none";
                }
            }
        }

        private int compute_zrc(double[] window2)
        {
            // TODO Auto-generated method stub
            int count = 0;
            for (i = 1; i <= BUFF_SIZE - 1; i++)
            {
                if ((window2[i] - th) < sigma && (window2[i - 1] - th) > sigma)
                {
                    count = count + 1;
                }
            }
            return count;
        }

        private async void SystemState(String curr_state1, String prev_state1)
        {
            //Fall !!
            if (prev_state1!=curr_state1)
            {
                await textStateText.Dispatcher.RunAsync(Windows.UI.Core.CoreDispatcherPriority.Normal, () =>
                {
                    textStateText.Text = curr_state1;
                }
                );
            }
        }

        private void AddData(double ax2, double ay2, double az2)
        {
            // TODO Auto-generated method stub
            a_norm = Math.Sqrt(ax2 * ax2 + ay2 * ay2 + az2 * az2);
            for (i = 0; i <= BUFF_SIZE - 2; i++)
            {
                window[i] = window[i + 1];
            }
            window[BUFF_SIZE - 1] = a_norm;
        }

    }
}