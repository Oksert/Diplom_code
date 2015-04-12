using System;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Collections.Generic;
using System.Data.SQLite;
using System.Data;
using System.Runtime.Serialization.Formatters.Binary;
using System.IO.Compression;

// State object for reading client data asynchronously
public class StateObject
{
    // Client  socket.
    public Socket workSocket = null;
    // Size of receive buffer.
    public const int BufferSize = 1024;
    // Receive buffer.
    public byte[] buffer = new byte[BufferSize];
    // Received data string.
    //public StringBuilder sb = new StringBuilder();
    public List<byte[]> bigBuf = new List<byte[]>();
    
    
}
public class AsynchronousSocketListener
{
    // Thread signal.
    public static ManualResetEvent allDone = new ManualResetEvent(false);
    public static ManualResetEvent fileIsBusy = new ManualResetEvent(false);
    public static SQLiteConnection connection;
    public AsynchronousSocketListener()
    {
    }
    public static DataSet DisSer(byte[] content)
    {
        using (System.IO.MemoryStream stream = new System.IO.MemoryStream(content))
        {
            stream.Position = 0;
            object desObj = new BinaryFormatter().Deserialize(stream);
            return desObj as DataSet;
        }
    }
    public static void fillTable(SQLiteConnection connection, byte[] binaryData)
    {
        var dt = DisSer(binaryData).Tables[0];
        foreach (DataRow dr in dt.Rows)
        {
            var comp = dr["COMP"].ToString();
            var user = dr["USER"].ToString();
            var site = dr["SITE"].ToString();
            var date = dr["DATE"].ToString() + DateTime.Now.Millisecond.ToString();
            var data = dr["DATA"].ToString();
            var cmd = connection.CreateCommand();
            cmd.Parameters.Add(new SQLiteParameter("@MachineName", comp));
            cmd.Parameters.Add(new SQLiteParameter("@UserName", user));
            cmd.Parameters.Add(new SQLiteParameter("@LocationURL", site));
            cmd.Parameters.Add(new SQLiteParameter("@DateTime", date));
            cmd.Parameters.Add(new SQLiteParameter("@Data", data));
            cmd.CommandType = CommandType.Text;
            cmd.CommandText = "insert into 'LOG' (COMP,USER,SITE,DATE,DATA) values (@MachineName, @UserName, @LocationURL, @DateTime, @Data)";
            cmd.ExecuteNonQuery();
        }
        
    }
    public static void StartListening()
    {
        // Data buffer for incoming data.
        byte[] bytes = new Byte[1024];

        // Establish the local endpoint for the socket.
        // The DNS name of the computer
        
        IPEndPoint localEndPoint = new IPEndPoint(IPAddress.Any, 8888);

        // Create a TCP/IP socket.
        Socket listener = new Socket(AddressFamily.InterNetwork,
            SocketType.Stream, ProtocolType.Tcp);

        // Bind the socket to the local endpoint and listen for incoming connections.
        try
        {
            listener.Bind(localEndPoint);
            listener.Listen(100);

            while (true)
            {
                // Set the event to nonsignaled state.
                allDone.Reset();

                // Start an asynchronous socket to listen for connections.
                Console.WriteLine("Waiting for a connection...");
                    listener.BeginAccept(
                        new AsyncCallback(AcceptCallback),
                        listener);
                // Wait until a connection is made before continuing.
                allDone.WaitOne();
            }

        }
        catch (Exception e)
        {
            Console.WriteLine(e.ToString());
        }

        Console.WriteLine("\nPress ENTER to continue...");
        Console.Read();

    }

    public static void AcceptCallback(IAsyncResult ar)
    {
        // Signal the main thread to continue.
        allDone.Set();

        // Get the socket that handles the client request.
        Socket listener = (Socket)ar.AsyncState;
        Socket handler = listener.EndAccept(ar);

        // Create the state object.
        StateObject state = new StateObject();
        state.workSocket = handler;
        handler.BeginReceive(state.buffer, 0, StateObject.BufferSize, 0,
            new AsyncCallback(ReadCallback2), state);
    }
    public static void ReadCallback2(IAsyncResult ar)
    {
        string content;
        StateObject state = (StateObject)ar.AsyncState;
        Socket handler = state.workSocket;

        // Read data from the client socket. 
        int bytesRead = 0;
        try
        {
            bytesRead = handler.EndReceive(ar);
        }
        catch (Exception e)
        {
            Console.WriteLine(e.Message);
        }
        if (bytesRead > 0)
        {
            // There  might be more data, so store the data received so far.
            //state.sb.Append(Encoding.UTF8.GetString(
            // state.buffer, 0, bytesRead));
            byte[] ins = new byte[bytesRead];
            for (int i = 0; i < ins.Length; i++)
            {
                ins[i] = state.buffer[i];
            }
           

            // Check for end-of-file tag. If it is not there, read 
            // more data.
            content = Encoding.UTF8.GetString(ins);
            if (content.IndexOf("<EOF>") > -1)
            {
                byte[] cutted = new byte[ins.Length - 5];
                Array.Copy(ins, cutted, ins.Length - 5);
                state.bigBuf.Add(cutted);
                Send(handler, "Data received");

                int len = 0;
                for (int i = 0; i < state.bigBuf.Count; i++)
                    len += state.bigBuf[i].Length;

                byte[] receivedData = new byte[len];
                len = 0;
                for (int i = 0; i < state.bigBuf.Count; i++)
                {
                    state.bigBuf[i].CopyTo(receivedData, len);
                    len += state.bigBuf[i].Length;
                }
                connectToDataBase("C:\\log2\\db.sqlite");
       
                fillTable(connection, receivedData);

            }
            else
            {
                state.bigBuf.Add(ins);
                //Not all data received. Get more.
                try
                {
                    //Console.WriteLine("Size is {0}", state.sb.Length);

                    handler.BeginReceive(state.buffer, 0, StateObject.BufferSize, 0,
                    new AsyncCallback(ReadCallback2), state);
                }
                catch (SocketException ex)
                {
                    Console.WriteLine(ex.Message);
                }
                //Console.WriteLine("No EndOfFile Character in file");
            }

        }
        else
        {
            handler.Shutdown(SocketShutdown.Both);
            handler.Close();
            Console.WriteLine("No EndOfFile Character in file");
        }
    }
    
    public static void ReadCallback(IAsyncResult ar)
    {
        
        String content = String.Empty;

        // Retrieve the state object and the handler socket
        // from the asynchronous state object.
        StateObject state = (StateObject)ar.AsyncState;
        Socket handler = state.workSocket;

        // Read data from the client socket. 
        int bytesRead = 0;
        try
        {
            bytesRead = handler.EndReceive(ar);
        }
        catch (Exception e)
        {
            Console.WriteLine(e.Message);
        }
        if (bytesRead > 0)
        {
            // There  might be more data, so store the data received so far.
            //state.sb.Append(Encoding.UTF8.GetString(
               // state.buffer, 0, bytesRead));
            byte[] ins = new byte[bytesRead];
            for (int i = 0; i < ins.Length; i++)
            {
                ins[i] = state.buffer[i];
            }
            state.bigBuf.Add(ins);

            // Check for end-of-file tag. If it is not there, read 
            // more data.
            content = Encoding.UTF8.GetString(state.bigBuf[state.bigBuf.Count - 1]);
            if (content.IndexOf("<EOF>") > -1)
            {
                // All the data has been read from the 
                // client. Display it on the console.
                //Console.WriteLine("Read {0} bytes from socket. \n Data : {1}",
                //    content.Length, content);
                // Echo the data back to the client.
                var fileNameLen = BitConverter.ToInt32(state.bigBuf[0], 0);
                string fileName = Encoding.UTF8.GetString(state.bigBuf[0], 4, fileNameLen);
                Send(handler, "Data received. Saved to E:\\" + fileName + ". " + DateTime.Now.ToString());
                
                //Console.WriteLine("filename: {0}", fileName);
                //var f = File.Open("e:\\" + fileName, FileMode.Create);
                fileIsBusy.Reset();
                BinaryWriter writer = new BinaryWriter(File.OpenWrite("e:\\" + fileName));
                string path = "E:\\" + fileName;
                for (int i = 0; i < state.bigBuf.Count; i++)
                {
                    if (i == 0)
                    {
                        writer.Write(state.bigBuf[i], 4 + fileNameLen, state.bigBuf[i].Length - (4 + fileNameLen));
                    }
                    else if (i == state.bigBuf.Count - 1)
                    {
                        writer.Write(state.bigBuf[i], 0, state.bigBuf[i].Length - Encoding.UTF8.GetBytes("<EOF>").Length);
                    }
                    else
                    {
                        writer.Write(state.bigBuf[i]);
                    }
                }
                writer.Close();
               fileIsBusy.Set();
                //Console.WriteLine(">> Data received. Saved to {0}", path);
               //Console.WriteLine("bigBuf[{0}][{1}]", state.bigBuf.Count, state.bigBuf[state.bigBuf.Count - 1].Length);
               // Console.WriteLine("Size = {0}", Encoding.UTF8.GetBytes(state.sb.ToString()).Length - (4 + fileNameLen + Encoding.UTF8.GetBytes("<EOF>").Length));
                
            }
            else
            {
                 //Not all data received. Get more.
                try 
                {
                    //Console.WriteLine("Size is {0}", state.sb.Length);

                    handler.BeginReceive(state.buffer, 0, StateObject.BufferSize, 0,
                    new AsyncCallback(ReadCallback), state);
                }
                catch(SocketException ex)
                {
                    Console.WriteLine(ex.Message);
                }
                //Console.WriteLine("No EndOfFile Character in file");
            }
           
        }
        else
        {
            
            Console.WriteLine("No EndOfFile Character in file");
        }
    }
    

    private static void Send(Socket handler, String data)
    {
        // Convert the string data to byte data using UTF8 encoding.
        byte[] byteData = Encoding.UTF8.GetBytes(data);
        // Begin sending the data to the remote device.
        handler.BeginSend(byteData, 0, byteData.Length, 0,
            new AsyncCallback(SendCallback), handler);
    }

    private static void SendCallback(IAsyncResult ar)
    {
        try
        {
            // Retrieve the socket from the state object.
            Socket handler = (Socket)ar.AsyncState;

            // Complete sending the data to the remote device.
            int bytesSent = handler.EndSend(ar);
            Console.WriteLine(">> Respond is sent to {0}", handler.RemoteEndPoint);

            handler.Shutdown(SocketShutdown.Both);
            handler.Close();

        }
        catch (Exception e)
        {
            Console.WriteLine(e.ToString());
        }
    }
    static public void connectToDataBase(string dataSourse)
    {
        if (!File.Exists(dataSourse))
        {
            SQLiteConnection.CreateFile(dataSourse);
            connection = new SQLiteConnection("Data Source=" + dataSourse +  ";Version=3;");
            connection.Open();
            var cmd = connection.CreateCommand();
            cmd.CommandText = "CREATE TABLE LOG (ID INTEGER PRIMARY KEY   AUTOINCREMENT,COMP TEXT , USER TEXT, SITE TEXT, DATE TEXT, DATA TEXT)";
            cmd.ExecuteNonQuery();
            connection.Close();

        }
        connection = new SQLiteConnection("Data Source=" + dataSourse + ";Version=3;");
        connection.Open();    
}

    public static int Main(String[] args)
    {

        
        connectToDataBase("C:\\log2\\db.sqlite");
       
        StartListening();
        
        return 0;
    }
}