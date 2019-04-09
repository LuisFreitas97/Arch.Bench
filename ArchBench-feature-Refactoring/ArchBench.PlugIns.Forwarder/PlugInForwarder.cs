using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using HttpServer;
using HttpServer.Sessions;

namespace ArchBench.PlugIns.Forwarder
{
    public class PlugInForwarder : IArchServerHTTPPlugIn
    {
        private readonly Dictionary<string,string> mServers = new Dictionary<string, string>();
        private readonly TcpListener mListener;
        private Thread mRegisterThread;

        public PlugInForwarder()
        {
            //AddServer( "sidoc", "127.0.0.1:8083" );
            mListener = new TcpListener(IPAddress.Any, 9000);
        }

        private void AddServer( string aName, string aUrl )
        {
            if ( mServers.ContainsKey( aName ))
            {
                mServers[aName] = aUrl;
                Host.Logger.WriteLine("The server url was updated for: {0}.", aUrl);
            }
            else
            {
                mServers.Add(aName, aUrl);
                Host.Logger.WriteLine("Added server {0}:{1}.", aName, aUrl);
            }
        }

        public void removeServer(string aName,string aUrl)
        {
            if (mServers.ContainsKey(aName))
            {
                mServers.Remove(aName);
                Host.Logger.WriteLine("Removed server {0}:{1}.", aName, aUrl);
            }
            else
            {
                Host.Logger.WriteLine("The server does not exist");
            }
        }

        private void ReceiveThreadFunction()
        {
            try
            {
                // Start listening for client requests.
                mListener.Start();

                // Buffer for reading data
                byte[] bytes = new byte[256];

                // Enter the listening loop.
                while (true)
                {
                    // Perform a blocking call to accept requests.
                    // You could also user server.AcceptSocket() here.
                    TcpClient client = mListener.AcceptTcpClient();

                    // Get a stream object for reading and writing
                    NetworkStream stream = client.GetStream();

                    int count = stream.Read(bytes, 0, bytes.Length);
                    if (count != 0)
                    {
                        // Translate data bytes to a ASCII string.
                        string data = Encoding.ASCII.GetString(bytes, 0, count);

                        char operation = data[0];
                        string uri = data.Substring(1, data.IndexOf('-', 1) - 1);
                        int portLength = data.IndexOf('-', data.IndexOf('-', 1) + 1) - (data.IndexOf('-', 1) + 1);
                        string port = data.Substring(data.IndexOf('-', 1) + 1, portLength);
                        uri = uri + ":" + port;
                        string serverName = data.Substring(data.IndexOf('-', data.IndexOf('-', 1) + 1) + 1);

                        switch (operation)
                        {
                            case '+':
                                AddServer(serverName,uri);
                                break;
                            case '-':
                                removeServer(serverName, uri);
                                break;
                        }
                    }
                    client.Close();
                }
            }
            catch (SocketException e)
            {
                Host.Logger.WriteLine("SocketException: {0}", e);
            }
            finally
            {
                mListener.Stop();
            }
        }

        #region IArchServerModulePlugIn Members

        public bool Process( IHttpRequest aRequest, IHttpResponse aResponse, IHttpSession aSession )
        {
            if ( mServers.ContainsKey(aRequest.UriParts[0]))
            {
                string sourceHost = $"{aRequest.Uri.Host}:{aRequest.Uri.Port}";
                string sourcePath = aRequest.UriPath;

                string targetHost = mServers[aRequest.UriParts[0]];
                string targetPath = aRequest.UriPath.Substring(aRequest.UriPath.IndexOf( '/', 1 ) );

                string targetUrl = $"http://{targetHost}{targetPath}";
                Uri uri = new Uri( targetUrl );

                Host.Logger.WriteLine( $"Forwarding request from server {sourceHost} to server {targetHost}" );

                WebClient client = new WebClient();
                try
                {
                    if ( aRequest.Headers["Cookie"] != null )
                    {
                        client.Headers.Add( "Cookie", aRequest.Headers["Cookie"] );    
                    }

                    byte[] bytes = null;
                    if ( aRequest.Method == Method.Post )
                    {
                        //String do tipo key valor
    	                NameValueCollection form = new NameValueCollection();
                        foreach ( HttpInputItem item in aRequest.Form )
                        {
                            form.Add( item.Name, item.Value );
                        }
                        //enviamos o pedido com o url e o form com os dados do formulário
		                bytes = client.UploadValues( uri, form );		
                    }
                    else//Caso seja um get fazemos o download dos dados.
                    {
                        bytes = client.DownloadData( uri );
                    }

                    //Verifica se já tem os cookies, se não tem coloca.
                    aResponse.ContentType = client.ResponseHeaders[HttpResponseHeader.ContentType];
                    if ( client.ResponseHeaders["Set-Cookie"] != null )
                    {
                        aResponse.AddHeader( "Set-Cookie", client.ResponseHeaders["Set-Cookie"] );
                    }

                    //Corrigir o valor de algumas tags no caso do content type da resposta ser igual a "text/html"
                    if ( aResponse.ContentType.StartsWith( "text/html" ) )
                    {
                        string data = client.Encoding.GetString( bytes );
                        data = data.Replace( targetHost, sourceHost + "/" + aRequest.UriParts[0] + "/" );
                        
                        data = data.Replace( "href=\"/", "href=\"/" + aRequest.UriParts[0] + "/" );
                        data = data.Replace( "src=\"/", "src=\"/" + aRequest.UriParts[0] + "/" );
                        data = data.Replace( "action=\"/", "action=\"/" + aRequest.UriParts[0] + "/" );

                        StreamWriter writer = new StreamWriter( aResponse.Body, client.Encoding );
                        writer.Write(data); writer.Flush();
                    }
                    else
                    {
                        aResponse.Body.Write(bytes, 0, bytes.Length);
                    }
                }
                catch (Exception e)
                {
                    Host.Logger.WriteLine( "Error on plugin Forwarder : {0}", e.Message );
                }

                return true;
            }

            return false;
        } 

        #endregion

        #region IArchServerPlugIn Members

        public string Name => "ArchServer Forwarder Plugin";

        public string Description => "Forward any request to port 8083";

        public string Author => "Leonel Nobrega";

        public string Version => "1.0";
        public bool Enabled { get; set; }

        public IDictionary<string, string> Parameters { get; } = new Dictionary<string, string>();

        public IArchServerPlugInHost Host { get; set; }

        public void Initialize()
        {
            mRegisterThread = new Thread(ReceiveThreadFunction);
            mRegisterThread.IsBackground = true;
            mRegisterThread.Start();
        }

        public void Dispose()
        {
        }

        #endregion
    }
}