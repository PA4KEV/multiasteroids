﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Net.Sockets;
using System.Threading;
using System.Net;
using AsteroidLibrary;
using System.IO;
using System.Collections;

namespace ServerMA
{
    class Program
    {
        //public delegate void OnClientAddedHandler(Object sender, ClientAddedMessage e);
        //public event OnClientAddedHandler OnClientAdded;

        private static int port = 6000;
        private Dictionary<TcpClient, StarshipClientData> clientQue;
        private int clientId;
        private int lobbyId;
        private Lobby lobby;

        static void Main(string[] args)
        {
            Program program = new Program();
            program.run();            
        }

        private void run()
        {
            Console.WriteLine("Server for MultiAsteroids game");
            this.clientQue = new Dictionary<TcpClient, StarshipClientData>();
            this.clientId = 1;
            this.lobbyId = 1;
            lobby = new Lobby(lobbyId);
            Console.WriteLine(string.Format("Lobby #{0} is open now", this.lobbyId));


            IPAddress ip;
            if (!IPAddress.TryParse("0.0.0.0", out ip))
                Console.WriteLine("cannot parse this IP");

            TcpListener listener = new TcpListener(ip, port);
            listener.Start();

            while (true)
            {
                TcpClient client = listener.AcceptTcpClient();
                Thread thread = new Thread(() => handleClientThread(client));
                thread.Name = "handling client";
                thread.Start();                
            }
        }

        private void handleClientThread(object obj)
        {
            TcpClient client = obj as TcpClient;
            StarshipClientData clientData = new StarshipClientData(clientId);
            bool running = false;
            string clientIp = ((IPEndPoint)client.Client.RemoteEndPoint).Address.ToString();            

            updateLobby(clientData, clientIp);

            this.clientQue.Add(client, clientData);                
            running = true;

            writeAddClientMessage(client);
            lobby.playersReady.Add(false);
            Console.WriteLine("Player {0} has been added!", clientId);
            this.clientId++; 

            while (running)
            {
                writeClientReadyStatus(client);
                readClientReadyStatus(client);
            }
            running = true;                      

            while (running)
            {
                try
                {
                    readMessage(client);
                    writeMovementMessage(client);
                }
                catch (IOException ex)
                {
                    Console.WriteLine(string.Format("ERROR({0}): {1}", clientIp, ex.Message));
                    running = false;
                }
                catch (InvalidOperationException ex)
                {
                    Console.WriteLine(string.Format("ERROR({0}): {1}", clientIp, ex.Message));
                    running = false;
                }
            }
        }

        private void updateLobby(StarshipClientData clientData, string clientIp)
        {
            if (lobby.OtherPlayersInLobby.Count == 0 && lobby.Host == null) //first in lobby            
            {
                lobby.Host = clientData;
                Console.WriteLine("Player {0} ({1}) entered lobby {2} as Host", clientData.ID, clientIp, lobbyId);
            }
            else if (lobby.OtherPlayersInLobby.Count < 3)
            {
                lobby.OtherPlayersInLobby.Add(clientData);
                Console.WriteLine("Player {0} ({1}) entered lobby {2} at spot {3}", clientData.ID, clientIp, lobbyId, (lobby.OtherPlayersInLobby.Count - 1));
            }
            else
                Console.WriteLine("ERROR({0}): cannot enter lobby", clientIp);            
        }

        private void readMessage(TcpClient client)
        {
            byte[] buffer = new byte[(clientQue.Count * 13) + 1];
            client.GetStream().Read(buffer, 0, buffer.Length);            
            switch ((int)buffer[0])
            {
                case (int)MessageType.Movement:
                    for (int i = 1; i < buffer.Length; i += 13) // per client
                    {
                        byte[] xAs = new byte[4];
                        byte[] yAs = new byte[4];
                        byte[] rot = new byte[4];
                        int player = (int)buffer[i];
                        for (int c = 0; c < 4; c++)
                            xAs[c] = buffer[(i + 1) + c];
                        for (int c = 0; c < 4; c++)
                            yAs[c] = buffer[(i + 5) + c];
                        for (int c = 0; c < 4; c++)
                            rot[c] = buffer[(i + 9) + c];

                        foreach (KeyValuePair<TcpClient, StarshipClientData> entry in clientQue)
                        {
                            if (entry.Value.ID == player)
                            {
                                entry.Value.Update(FloatUnion.BytesToFloat(xAs), FloatUnion.BytesToFloat(yAs), FloatUnion.BytesToFloat(rot));
                            }
                            break;
                        }
                    }
                    break;                
            }            
        }

        private void writeMovementMessage(TcpClient client)
        {
            List<byte> data = new List<byte>();
            data.Add((int)MessageType.Movement); // Message type byte
            foreach (KeyValuePair<TcpClient, StarshipClientData> entry in clientQue)
            {   
                data.Add((byte)entry.Value.ID); // Player number byte
                foreach (byte b in FloatUnion.FloatToBytes(entry.Value.X))
                    data.Add(b);
                foreach (byte b in FloatUnion.FloatToBytes(entry.Value.Y))
                    data.Add(b);
                foreach (byte b in FloatUnion.FloatToBytes(entry.Value.Rotation))
                    data.Add(b);             
            }
            client.GetStream().Write(data.ToArray(), 0, data.Count);
            client.GetStream().Flush();  
        }

        // change to delegates?
        private void writeAddClientMessage(TcpClient client)
        {
            List<byte> data = new List<byte>();
            data.Add((int)MessageType.AddedClient);
            data.Add((byte)clientId);
            client.GetStream().Write(data.ToArray(), 0, data.Count);
            client.GetStream().Flush();
        }

        private void writeClientReadyStatus(TcpClient client)
        {
            List<byte> data = new List<byte>();
            data.Add((int)MessageType.PlayerReadyStatus);
            data.Add((byte)lobby.OtherPlayersInLobby.Count);
            data.Add((byte)lobby.allPlayersReady());
            client.GetStream().Write(data.ToArray(), 0, data.Count);
            client.GetStream().Flush();
        }

        private void readClientReadyStatus(TcpClient client)
        {
            try
            {
                byte[] buffer = new byte[client.ReceiveBufferSize];
                List<byte> data = new List<byte>();
                client.GetStream().Read(buffer, 0, client.ReceiveBufferSize);
                switch ((int)buffer[0])
                {
                    case (int)MessageType.PlayerReadyStatus:
                        lobby.playersReady[buffer[2] -1] = (buffer[1] == 1); // true or false
                        break;
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine("ERROR: " + ex.Message);
            }
        }
    }
}
