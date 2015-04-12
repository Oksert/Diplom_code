using System;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Text;
using System.IO;
using System.Runtime.Serialization.Formatters.Binary;
using System.Data;
using System.IO.Compression;
using System.Data.SQLite;
using System.Xml;
// State object for receiving data from remote device.
public class StateObject
{
    // Client socket.
    public Socket workSocket = null;
    // Size of receive buffer.
    public const int BufferSize = 256;
    // Receive buffer.
    public byte[] buffer = new byte[BufferSize];
    // Received data string.
    public StringBuilder sb = new StringBuilder();
}

public class AsynchronousClient
{
    // The port number for the remote device.
    private const int port = 8888;
    public static string fileName;
    // ManualResetEvent instances signal completion.
    private static ManualResetEvent connectDone =
        new ManualResetEvent(false);
    private static ManualResetEvent sendDone =
        new ManualResetEvent(false);
    private static ManualResetEvent receiveDone =
        new ManualResetEvent(false);
    public static string ipAddr;


    // The response from the remote device.
    private static String response = String.Empty;
    //serialize
    public static byte[] SerializeObj(object obj)
    {
        using (System.IO.MemoryStream stream = new System.IO.MemoryStream())
        {
            BinaryFormatter formatter = new System.Runtime.Serialization.Formatters.Binary.BinaryFormatter();
            using (var ds = new DeflateStream(stream, CompressionMode.Compress, true))
                formatter.Serialize(stream, obj);

            stream.Flush();
            stream.Position = 0;
            byte[] bytes = stream.ToArray();

            return bytes;
        }
    }

    //deserialize
    public static object DeserializeObj(byte[] binaryObj)
    {
        using (System.IO.MemoryStream stream = new System.IO.MemoryStream(binaryObj))
            using (var ds = new DeflateStream(stream, CompressionMode.Decompress, true))
                return new BinaryFormatter().Deserialize(ds);
    }

    public static byte[] Serialize(DataSet set)
    {
        //return Encoding.UTF8.GetBytes(set.GetXml());
        /*
        using (System.IO.MemoryStream ms = new System.IO.MemoryStream())
        {
            set.WriteXml(ms, XmlWriteMode.WriteSchema);
            ms.Flush();
            ms.Position = 0;
            byte[] bytes = ms.ToArray();

            return bytes;
        }
          */
        return SerializeObj(set);
        /*
        try
        {
            var formatter = new BinaryFormatter(); //null, new System.Runtime.Serialization.StreamingContext(System.Runtime.Serialization.StreamingContextStates.CrossMachine));
            byte[] content;
            using (var ms = new MemoryStream())
            {
                //using (var ds = new DeflateStream(ms, CompressionMode.Compress, true))
               // {
                formatter.Serialize(ms, set);
                //}
                ms.Position = 0;
                content = ms.GetBuffer();
                //contentAsString = BytesToString(content);
                return content;
            }
        }
        catch (Exception ex) { }
        return null;
        */
    }
    private static void StartClient()
    {
        // Connect to a remote device.
        try
        {
            string timestamp;
            byte[] serDataBase;
            string connStr = "Data Source=C:\\log\\db_" + Environment.UserName + ".sqlite; Version=3;";
            using (SQLiteConnection con = new SQLiteConnection(connStr))
            {
                con.Open();
                SQLiteDataAdapter adapter = new SQLiteDataAdapter("select * from LOG order by DATE", con);
                DataSet table = new DataSet();
                adapter.Fill(table);
                timestamp = table.Tables[0].Rows.Count > 0
                    ? table.Tables[0].Rows[table.Tables[0].Rows.Count - 1][table.Tables[0].Columns["DATE"].Ordinal].ToString()
                    : null;
                serDataBase = Serialize(table);
            }

            byte[] eof = Encoding.UTF8.GetBytes("<EOF>");
            byte[] toSend = new byte[serDataBase.Length + eof.Length];
            serDataBase.CopyTo(toSend, 0);
            eof.CopyTo(toSend, serDataBase.Length);
            using (FileStream fs = new FileStream("xxx", FileMode.Create))
                fs.Write(toSend, 0, toSend.Length);

            IPAddress ip = IPAddress.Parse(ipAddr);
            IPEndPoint remoteEP = new IPEndPoint(ip, 8888);
            bool isOk = false;
            using (Socket client = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp))
            {
                try
                {
                    client.Connect(remoteEP);
                    Console.WriteLine(">> Socket connected to {0}",
                        client.RemoteEndPoint.ToString());
                    int bytesSent = client.Send(toSend);
                    Console.WriteLine(">> Sent {0} bytes to server.", bytesSent);
                    byte[] bytes = new byte[16536];
                    int bytesRec = client.Receive(bytes);
                    Console.WriteLine("Received from server: {0}", Encoding.UTF8.GetString(bytes, 0, bytesRec));
                    client.Shutdown(SocketShutdown.Both);
                    client.Close();
                    isOk = true;
                }
                catch (ArgumentNullException ane)
                {
                    Console.WriteLine("ArgumentNullException : {0}", ane.ToString());
                }
                catch (SocketException se)
                {
                    Console.WriteLine("SocketException : {0}", se.ToString());
                }
                catch (Exception e)
                {
                    Console.WriteLine("Unexpected exception : {0}", e.ToString());
                }
            }

            if (isOk)
                using (SQLiteConnection con = new SQLiteConnection(connStr))
                {
                    con.Open();
                    using (var cmd = con.CreateCommand())
                    {
                        if (timestamp != null)
                        {
                            Console.WriteLine(DateTime.Parse(timestamp));
                            cmd.CommandText = "Delete from LOG where DATE <= @timestamp";
                            cmd.Parameters.Add(new SQLiteParameter("@timestamp", timestamp));
                            cmd.ExecuteNonQuery();
                        }
                        cmd.CommandText = "vacuum";
                        cmd.ExecuteNonQuery();
                    }
                    // Write the response to the console.
                    // Console.WriteLine(">> Response received :\n\t {0}", response);
                }
        }
        catch (Exception e)
        {
            Console.WriteLine(e.ToString());
            Console.ReadLine();
        }
    }

    public static int Main(String[] args)
    {
        Console.WriteLine(">> Enter Server IpAdrees");
        ipAddr = Console.ReadLine();
        Timer t = new Timer(TimerCallback, null, 0, 10000);
        Thread.Sleep(System.Threading.Timeout.Infinite);
        return 0;
    }

    public static void TimerCallback(Object o)
    {
        try
        {
            StartClient();
        }
        catch (Exception e)
        {
            Console.WriteLine(e.Message);
            StartClient();
        }
    }
}