﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Net;
using System.Threading;
using System.Globalization;

namespace LEASDKCS
{
    class Program
    {
        static string pass = "123456";
        static int port = 45612;
        static string gameName = "LEA SDK";
        static string projectName = "LEA SDK";

        static bool connected = false;
        static AutoResetEvent ARE = new AutoResetEvent(false);
        static char[] space = new char[] { ' ' };
        static CultureInfo usCulture = new CultureInfo("en-US");

        static void Main(string[] args)
        {
            Console.WriteLine("Input the current password");
            pass = Console.ReadLine();
            Console.WriteLine();

            TCPLayerLite.localDeviceType = TCPLayerLite.deviceType.GAME;
            TCPLayerLite.DataReceived += commands.newDataToDecode;
            TCPLayerLite.FailToConnect += TCPLayerLite_FailToConnect;
            TCPLayerLite.DirectConnectionEstablished += TCPLayerLite_ConnectionEstablished;
            TCPLayerLite.LastConnectionLost += TCPLayerLite_LastConnectionLost;
            TCPLayerLite.NoConnectedDevice += TCPLayerLite_NoConnectedDevice;
            TCPLayerLite.setDefaultSecurityOptions(TCPLayerLite.securityMode.PASS_SHA1, Encoding.UTF8.GetBytes("Anonymous"), Encoding.UTF8.GetBytes(pass), false);
            TCPLayerLite.launchConnection(new IPEndPoint(IPAddress.Loopback, port));

            if (!ARE.WaitOne(10000))
            {
                Console.WriteLine("Fail to get answer from server.");
                Thread.Sleep(5000); // <- for seeing previous message, it has nothing to do with the TCPLayerLite
                TCPLayerLite.shutdownAll();
                return;
            }
            if (!connected)
            {
                Console.WriteLine("Connection closed. Bad port or bad password.");
                Thread.Sleep(5000); // <- for seeing previous message, it has nothing to do with the TCPLayerLite
                TCPLayerLite.shutdownAll();
                return;
            }

            Console.WriteLine("Check if sample game is installed.");

            commands.GameListReceived += commands_GameListReceived;
            commands.sendGetGames();
            if (!ARE.WaitOne(1000))
            {
                Console.WriteLine("Fail to get answer from server.");
                Thread.Sleep(5000); // <- for seeing previous message, it has nothing to do with the TCPLayerLite
                TCPLayerLite.shutdownAll();
                return;
            }
            Console.WriteLine();

            Console.WriteLine("Check if sample project is installed.");

            commands.ProjectListReceived += commands_ProjectListReceived;
            commands.sendGetProjectNames(gameName);
            if (!ARE.WaitOne(1000))
            {
                Console.WriteLine("Fail to get answer from server.");
                Thread.Sleep(5000); // <- for seeing previous message, it has nothing to do with the TCPLayerLite
                TCPLayerLite.shutdownAll();
                return;
            }
            Console.WriteLine();

            Console.WriteLine("Choose the server mode and press enter.");
            Console.WriteLine("  1: push mode. The server update this program when it get any input from clients.");
            Console.WriteLine("  2: pull mode. This program ask the server for updates.");
            bool pushMode = false;
            bool pullMode = false;
            while (!pullMode && !pushMode)
            {
                string input = Console.ReadLine();
                if (input == "1")
                {
                    pushMode = true;
                }
                else if (input == "2")
                {
                    pullMode = true;
                }
            }
            Console.WriteLine();

            bool continuousMode = false;
            bool requestMode = false;

            if (pushMode)
            {
                Console.WriteLine("Setting server mode to push.");
                commands.sendConfigureServer(commands.serverMode.PUSH); // <- send configuration before setting the EMTag list

                Console.WriteLine("Choose display mode and press enter.");
                Console.WriteLine("  1: continuous. A new incoming input fire a event.");
                Console.WriteLine("  2: on request. You must ask the local cache for change with the \"get EMTagName\" (replace the EMTagName with button1, slider1, etc.) command.");
                
                while (!continuousMode && !requestMode)
                {
                    string input = Console.ReadLine();
                    if (input == "1")
                    {
                        continuousMode = true;
                    }
                    else if (input == "2")
                    {
                        requestMode = true;
                    }
                }
                if (continuousMode)
                {
                    commands.NewCommands += commands_NewCommands;
                }
            }
            else if (pullMode)
            {
                Console.WriteLine("Setting server mode to pull.");
                commands.sendConfigureServer(commands.serverMode.PULL); // <- send configuration before setting the EMTag list

                Console.WriteLine("Choose display mode and press enter.");
                Console.WriteLine("  1: continuous (on internal timer tick). The program ask every X ms the server for changes and update the local cache, and fire a new event if an input changed.");
                Console.WriteLine("  2: on request. You must ask manually the server for update and fire an event if an input changed.");

                while (!continuousMode && !requestMode)
                {
                    string input = Console.ReadLine();
                    if (input == "1")
                    {
                        continuousMode = true;
                    }
                    else if (input == "2")
                    {
                        requestMode = true;
                    }
                }
                if (continuousMode)
                {
                    Console.WriteLine();
                    Console.WriteLine("Choose the timer interval in ms (integer).");
                    string input = Console.ReadLine();
                    int value = -1;
                    while (value < 0)
                    {
                        if (!int.TryParse(input, NumberStyles.Integer, usCulture, out value)) 
                        {
                            Console.WriteLine("Error while parsing value.");
                        }
                    }
                    Console.WriteLine("Configuring timer.");
                    commands.setPullTimerParams(value);
                    commands.NewCommands += commands_NewCommands;
                }
                if (requestMode)
                {
                    Console.WriteLine();
                    Console.WriteLine("Type pull to get data from server.");
                    Console.WriteLine("WARNING! You must check the server at least every 30s. Fail to do that may lead to desynch between clients and game.");
                    commands.NewCommands += commands_NewCommands;
                }
            }

            Console.WriteLine();
            Console.WriteLine("Registering the EMTags: button1, toggle1, slider1, POV1, buttonPOV1, buttonPOV2");
            List<commands.EMData> EMDList = new List<commands.EMData>();
            EMDList.Add(new commands.EMData("button1", "False", commands.EMType.BUTTON));
            EMDList.Add(new commands.EMData("toggle1", "False", commands.EMType.BUTTON));
            EMDList.Add(new commands.EMData("slider1", "0.5;0.5", commands.EMType.AXIS));
            EMDList.Add(new commands.EMData("POV1", "-1", commands.EMType.POV));
            EMDList.Add(new commands.EMData("buttonPOV1", "False", commands.EMType.BUTTON));
            EMDList.Add(new commands.EMData("buttonPOV2", "False", commands.EMType.BUTTON));
            commands.registerCommands(EMDList);

            Console.WriteLine();
            Console.WriteLine("Select game mode.");
            Console.WriteLine("  1 for game mode (joystick remap rules apply).");
            Console.WriteLine("  2 for plugin mode (no joystick remap rules apply).");
            bool gameMode = false;
            bool pluginMode = false;
            while (!gameMode && !pluginMode)
            {
                string input = Console.ReadLine();
                if (input == "1")
                {
                    gameMode = true;
                }
                else if (input == "2")
                {
                    pluginMode = true;
                }
            }
            if (gameMode)
            {
                commands.sendRealJoyRemapApplyRules(projectName, gameName);
            }

            if (pullMode && continuousMode)
            {
                Console.WriteLine("Starting timer.");
                commands.startPullTimer();
            }

            Console.WriteLine("Resynch.");
            commands.sendResynchData();

            Console.WriteLine("Ready.");

            while (true)
            {
                string input = Console.ReadLine();
                if (input == "exit")
                {
                    break;
                }
                else if (input == "pull")
                {
                    commands.sendPullRequest();
                }
                else if (input == "resynch") // for debug purpose only
                {
                    commands.sendResynchData();
                }
                else
                {
                    string[] splitInput = input.Split(space, StringSplitOptions.RemoveEmptyEntries);
                    if (splitInput.Length > 1)
                    {
                        if (splitInput[0] == "get")
                        {
                            string newValue = commands.getValueOfCommand(splitInput[1]);
                            if (newValue == null)
                            {
                                Console.WriteLine("No command with the specified EMTag is registered.");
                            }
                            else
                            {
                                Console.WriteLine(newValue);
                            }
                        }
                        else if (splitInput[0] == "pull")
                        {
                            List<string> EMTags = new List<string>(splitInput.Length - 1);
                            for (int i = 1; i < splitInput.Length; i++)
                            {
                                EMTags.Add(splitInput[i]);
                            }
                            commands.sendPullRequest(EMTags);
                        }
                        else if (splitInput[0] == "set" && splitInput.Length > 2)
                        {
                            commands.EMType? curType = commands.getTypeOfCommand(splitInput[1]);
                            if (curType == null)
                            {
                                Console.WriteLine("No command with the specified EMTag is registered.");
                            }
                            else
                            {
                                commands.EMData EMD = new commands.EMData();
                                EMD.EMTag = splitInput[1];
                                EMD.EMValue = splitInput[2];
                                EMD.type = (commands.EMType)curType;
                                commands.sendPushData(new List<commands.EMData> { EMD });
                            }
                            
                        }
                    }
                }
            }

            commands.stopPullTimer();
            TCPLayerLite.shutdownAll();
        }

        //////////////////////////////////////////////////////////////////////////
        //TCP layer events
        //////////////////////////////////////////////////////////////////////////

        static void TCPLayerLite_FailToConnect(List<TCPLayerLite.device> devList)
        {
            Console.WriteLine("Fail to connect to server.");
            connected = false;
            ARE.Set();
        }

        static void TCPLayerLite_ConnectionEstablished(List<TCPLayerLite.device> devList)
        {
            Console.WriteLine("Connection established.");
            connected = true;
            ARE.Set();
        }

        static void TCPLayerLite_LastConnectionLost(List<TCPLayerLite.device> devList)
        {
            Console.WriteLine("Connection lost.");
            connected = false;
            ARE.Set();
        }

        static void TCPLayerLite_NoConnectedDevice()
        {
            Console.WriteLine("Cannot send data: no connection.");
        }

        //////////////////////////////////////////////////////////////////////////
        //Commands events
        //////////////////////////////////////////////////////////////////////////

        static void commands_GameListReceived(List<string> list)
        {
            bool found = false;
            foreach (string game in list)
            {
                if (game == gameName)
                {
                    found = true;
                    break;
                }
            }
            if (!found)
            {
                Console.WriteLine("Game is not set. Setting new game.");
                commands.sendNewGame(gameName);
            }
            else
            {
                Console.WriteLine("Game is already set.");
            }
            ARE.Set();
        }

        static void commands_ProjectListReceived(List<string> list)
        {
            bool found = false;
            foreach (string project in list)
            {
                if (project == projectName)
                {
                    found = true;
                    break;
                }
            }
            if (!found)
            {
                Console.WriteLine("Project is not set. Setting project with XML.");
                byte[] data;
                string path = System.IO.Path.GetDirectoryName(System.Reflection.Assembly.GetExecutingAssembly().Location) + 
                    System.IO.Path.DirectorySeparatorChar + "SDKSample.ppk";
                if (!System.IO.File.Exists(path))
                {
                    Console.WriteLine("No PPK file found. Please put the SDKSample.ppk file in your executing directory.");
                    return;
                }
                try
                {
                    data = System.IO.File.ReadAllBytes(path);
                }
                catch (System.Exception ex)
                {
                    Console.WriteLine("Error while reading the SDKSample.ppk file. Error: " + ex.Message);
                    return;
                }
                commands.sendSetProjectArchive(projectName, gameName, data);
            }
            else
            {
                Console.WriteLine("Project is already set.");
            }
            ARE.Set();
        }

        static void commands_NewCommands(List<commands.EMData> EMDList)
        {
            foreach (commands.EMData EMD in EMDList)
            {
                Console.WriteLine("Command: " + EMD.EMTag + "; Value: " + EMD.EMValue);
            }
        }
    }
}
