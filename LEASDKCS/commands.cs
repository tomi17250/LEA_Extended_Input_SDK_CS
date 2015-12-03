using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Xml;
using System.Globalization;
using System.IO;

namespace LEASDKCS
{
    internal static class commands
    {
        static char[] semicolon = new char[] { ';' };

        public enum commandType
        {
            CHECK_CONNECTION,
            CHECK_CONNECTION_ANSWER,

            GET_CONFIG,
            GET_CONFIG_ANSWER,

            GAME_LIST,
            GAME_NEW,
            GAME_COPY,
            GAME_RENAME,
            GAME_DELETE,
            GAME_ARCHIVE_GET,
            GAME_ARCHIVE_GET_ALL,
            GAME_ARCHIVE_SET,
            GAME_GET_BASIC_DATA,
            GAME_NEW_CONTENT,
            GAME_SET_DESCRIPTION,

            PROJECT_LIST,
            PROJECT_DOWNLOAD,
            PROJECT_DOWNLOAD_ALL,
            PROJECT_UPLOAD,
            PROJECT_DELETE,
            PROJECT_COPY,
            PROJECT_RENAME,
            PROJECT_ARCHIVE_GET,
            PROJECT_ARCHIVE_GET_ALL,
            PROJECT_ARCHIVE_SET,
            PROJECT_GET_BASIC_DATA,
            PROJECT_NEW_CONTENT,
            PROJECT_SET_DESCRIPTION,

            CTRL_TEMPLATE_LIST,
            CTRL_TEMPLATE_DOWNLOAD,
            CTRL_TEMPLATE_DOWNLOAD_ALL,
            CTRL_TEMPLATE_UPLOAD,
            CTRL_TEMPLATE_DELETE,
            CTRL_TEMPLATE_COPY,
            CTRL_TEMPLATE_RENAME,
            CTRL_TEMPLATE_ARCHIVE_GET,
            CTRL_TEMPLATE_ARCHIVE_GET_ALL,
            CTRL_TEMPLATE_ARCHIVE_SET,
            CTRL_TEMPLATE_GET_BASIC_DATA,
            CTRL_TEMPLATE_NEW_CONTENT,
            CTRL_TEMPLATE_SET_DESCRIPTION,

            RESOURCE_LIST,
            RESOURCE_DOWNLOAD,
            RESOURCE_DOWNLOAD_ALL,
            RESOURCE_UPLOAD,
            RESOURCE_DELETE,
            RESOURCE_COPY,
            RESOURCE_RENAME,
            RESOURCE_ARCHIVE_GET,
            RESOURCE_ARCHIVE_GET_ALL,
            RESOURCE_ARCHIVE_SET,
            RESOURCE_GET_BASIC_DATA,
            RESOURCE_NEW_CONTENT,
            RESOURCE_SET_DESCRIPTION,

            COMMAND_BLOCK,

            VJOY_SET_CONFIG,
            VJOY_SET_BUTTON,
            VJOY_SET_HAT,
            VJOY_SET_AXIS,
            VJOY_RESET,
            VJOY_RESYNCH,

            EXTINPUT_PULL_DATA,
            EXTINPUT_PUSH_DATA,
            EXTINPUT_RESYNCH_DATA,
            EXTINPUT_CONFIGURE_SERVER,

            SETTERS_PUSH,

            ENTER_EDIT_MODE,
            EXIT_EDIT_MODE,

            REAL_JOY_REMAP_GET_AVAILABLE_JOY,
            REAL_JOY_REMAP_SET_AVAILABLE_JOY,
            REAL_JOY_REMAP_RULES_UPLOAD,
            REAL_JOY_REMAP_RULES_DOWNLOAD,
            REAL_JOY_REMAP_RULES_RESET,
            REAL_JOY_REMAP_APPLY_RULES,

            GET_VERSION_INFOS,
            SET_VERSION_INFOS,
            GET_VERSION_FOR_CHECKING,
            SET_VERSION_FOR_CHECKING,

            VIRTUAL_KEYBOARD_ENABLE_REQUEST,
            VIRTUAL_KEYBOARD_DISABLE_REQUEST,
            VIRTUAL_KEYBOARD_SIMULATE_KEY,

            SET_SPECIFIED_PROGRAM_MAINWIN_TO_FOREGROUND,
            SEND_WARNING,
            SEND_ERROR,

            SET_ADDITIONAL_FILE,
            GET_ADDITIONAL_FILE,
            REMOVE_ADDITIONAL_FILE,
            GET_ADDITIONAL_FILES,

            VIRTUAL_KEYBOARD_SIMULATE_TEXT_INPUT,
            VIRTUAL_KEYBOARD_SIMULATE_MODIFIED_KEY,

            GET_VIRTUAL_KEYBOARD_REMAP_RULES,
            SET_VIRTUAL_KEYBOARD_REMAP_RULES,
            SET_VIRTUAL_KEYBOARD_REMAP_RULE_NAME_IN_GAME,
        }

        public enum serverMode
        {
            PULL,
            PUSH,
        }

        public enum EMType
        {
            NONE = 0,
            BUTTON,
            AXIS,
            POV,

        }

        /// <summary>
        /// Extended Input data block
        /// </summary>
        public class EMData
        {
            /// <summary>
            /// ID of Extended Input block
            /// </summary>
            public string EMTag;
            /// <summary>
            /// Value of Extended Input block
            /// </summary>
            public string EMValue;
            /// <summary>
            /// Type of Extended Input block
            /// </summary>
            public EMType type;

            public EMData() { }
            public EMData(string EMTagArg, string EMValueArg, EMType typeArg)
            {
                EMTag = EMTagArg;
                EMValue = EMValueArg;
                type = typeArg;
            }

            /// <summary>
            /// Populate fields from XML data
            /// </summary>
            /// <param name="xn">node to use</param>
            /// <returns>true if succeeded</returns>
            public bool FromXml(XmlNode xn)
            {
                if (xn.Name != "EMData")
                {
                    return false;
                }
                foreach (XmlAttribute xa in xn.Attributes)
                {
                    if (xa.Name == "EMTag")
                    {
                        EMTag = xa.Value;
                    }
                    else if (xa.Name == "EMValue")
                    {
                        EMValue = xa.Value;
                    }
                    else if (xa.Name == "type")
                    {
                        int value;
                        if (int.TryParse(xa.Value, NumberStyles.Integer, usCulture, out value))
                        {
                            type = (commands.EMType)value;
                        }
                    }
                }
                return true;
            }

            /// <summary>
            /// Append data of this block to the specified XML Node
            /// </summary>
            /// <param name="xn">The reference of the node to which to append data</param>
            public void appendToXmlNode(ref XmlNode xn)
            {
                XmlDocument XD = xn.OwnerDocument;

                XmlNode root = XD.CreateElement("EMData");
                xn.AppendChild(root);

                XmlAttribute xa = XD.CreateAttribute("EMTag");
                xa.Value = EMTag;
                root.Attributes.Append(xa);

                xa = XD.CreateAttribute("EMValue");
                xa.Value = EMValue;
                root.Attributes.Append(xa);

                xa = XD.CreateAttribute("type");
                xa.Value = ((int)type).ToString("D");
                root.Attributes.Append(xa);
            }

            /// <summary>
            /// Copy this Extended Input data block to a new one
            /// </summary>
            /// <returns></returns>
            public EMData Copy()
            {
                EMData EMD = new EMData();
                EMD.EMTag = string.Copy(EMTag);
                EMD.EMValue = string.Copy(EMValue);
                EMD.type = type;
                return EMD;
            }
        }

        static AutoResetEvent incomingData;
        static CultureInfo usCulture;

        static List<EMData> commandStatus;
        static Timer pullTimer;
        static bool pullTimerEnabled;

        /// <summary>
        /// Incoming Extended Input commands handler
        /// </summary>
        /// <param name="EMDList">List of incomming commands</param>
        public delegate void CommandEventHandler(List<EMData> EMDList);

        /// <summary>
        /// Some new commands has been received and decoded.
        /// </summary>
        public static event CommandEventHandler NewCommands;

        static commands()
        {
            incomingData = new AutoResetEvent(false);
            usCulture = new CultureInfo("en-US");

            commandStatus = new List<EMData>();
            pullTimerEnabled = false;
            pullTimer = new Timer(pullTimerCallback, null, Timeout.Infinite, Timeout.Infinite);

            Thread decodeCommandThread = new Thread(commandDecode);
            decodeCommandThread.IsBackground = true;
            decodeCommandThread.Start();
        }

        static void pullTimerCallback(object state)
        {
            if (pullTimerEnabled)
            {
                sendPullRequest();
            }
        }

        /// <summary>
        /// Use this method to start decoding data. Use it with the TCPLayerLite.DataReceived event.
        /// </summary>
        public static void newDataToDecode()
        {
            incomingData.Set();
        }

        /// <summary>
        /// Configure the interval of the pull timer. 
        /// </summary>
        /// <param name="periodMS">period in ms</param>
        public static void setPullTimerParams(int periodMS)
        {
            pullTimer.Change(0, periodMS);
        }

        /// <summary>
        /// Enable the pull timer.
        /// </summary>
        public static void startPullTimer()
        {
            pullTimerEnabled = true;
        }

        /// <summary>
        /// Disable the pull timer.
        /// </summary>
        public static void stopPullTimer()
        {
            pullTimerEnabled = false;
        }

        /// <summary>
        /// Get all registered commands and their associated current values.
        /// </summary>
        /// <returns>A list of extended input command block</returns>
        public static List<EMData> getAllCommands()
        {
            List<EMData> retList;
            lock (commandStatus)
            {
                retList = new List<EMData>(commandStatus.Count);
                foreach (EMData EMD in commandStatus)
                {
                    retList.Add(EMD.Copy());
                }
            }
            return retList;
        }

        /// <summary>
        /// Get the current value of the specified extended input command.
        /// </summary>
        /// <param name="EMTag">The ID of the extended input command</param>
        /// <returns>the value of this command</returns>
        public static string getValueOfCommand(string EMTag)
        {
            lock (commandStatus)
            {
                foreach (EMData EMD in commandStatus)
                {
                    if (EMD.EMTag == EMTag)
                    {
                        return EMD.EMValue;
                    }
                }
            }
            return null;
        }

        /// <summary>
        /// get the specified extended input command type
        /// </summary>
        /// <param name="EMTag"></param>
        /// <returns>null if the registered extended input command is not found, else its type</returns>
        public static EMType? getTypeOfCommand(string EMTag)
        {
            lock (commandStatus)
            {
                foreach (EMData EMD in commandStatus)
                {
                    if (EMD.EMTag == EMTag)
                    {
                        return EMD.type;
                    }
                }
            }
            return null;
        }

        /// <summary>
        /// Get the specified extended input commands
        /// </summary>
        /// <param name="EMTags">list of extended input commands to get</param>
        /// <returns>A list of extended input command block</returns>
        public static List<EMData> getValueOfCommands(List<string> EMTags)
        {
            List<EMData> retList;
            lock (commandStatus)
            {
                retList = new List<EMData>(commandStatus.Count);
                foreach (EMData EMD in commandStatus)
                {
                    if (EMTags.Contains(EMD.EMTag))
                    {
                        retList.Add(EMD.Copy());
                    }
                }
            }
            return retList;
        }

        /// <summary>
        /// Register a list extended input commands. This will clear all previously registered extended input commands.
        /// </summary>
        /// <param name="EMDList">the list of extended input data block to register</param>
        public static void registerCommands(List<EMData> EMDList)
        {
            lock (commandStatus)
            {
                commandStatus.Clear();
                commandStatus.AddRange(EMDList);
            }
        }

        /// <summary>
        /// Append a list extended input commands to the already registered extended input commands.
        /// </summary>
        /// <param name="EMDList">the list of extended input data block to register</param>
        public static void registerAdditionnalCommands(List<EMData> EMDList)
        {
            lock (commandStatus)
            {
                commandStatus.AddRange(EMDList);
            }
        }

        /// <summary>
        /// Unregister a list extended input commands from the already registered extended input commands.
        /// </summary>
        /// <param name="EMTagsToUnregister">the list of extended input data block to unregister</param>
        public static void unregisterSpecifiedCommands(List<string> EMTagsToUnregister)
        {
            lock (commandStatus)
            {
                for (int i = 0; i < commandStatus.Count; )
                {
                    EMData EMD = commandStatus[i];
                    if (EMTagsToUnregister.Contains(EMD.EMTag))
                    {
                        commandStatus.RemoveAt(i);
                    }
                    else
                    {
                        i++;
                    }
                }
            }
        }

        /// <summary>
        /// Unregister all extended input commands
        /// </summary>
        public static void clearCommandList()
        {
            lock (commandStatus)
            {
                commandStatus.Clear();
            }
        }

        static void commandDecode()
        {
            while (true)
            {
                bool noExternData = true;
                bool noLocalData = true;
                TCPLayerLite.dataBlock DB = TCPLayerLite.getFirstDataBlock(TCPLayerLite.deviceType.SERVER);
                if (DB != null)
                {
                    noLocalData = false;
                    localDecode(DB);
                }
                if (noExternData && noLocalData)
                {
                    incomingData.WaitOne();
                }
            }
        }

        /// <summary>
        /// The received game list event handler.
        /// </summary>
        /// <param name="list">The names of all installed games on server</param>
        public delegate void GameListReceivedHandler(List<string> list);
        /// <summary>
        /// Fire when a game list is received
        /// </summary>
        public static event GameListReceivedHandler GameListReceived;

        /// <summary>
        /// The received game archive event handler.
        /// </summary>
        /// <param name="data">The PPK archive</param>
        public delegate void GameArchiveReceivedHandler(byte[] data);
        /// <summary>
        /// Fire when a game archive is received
        /// </summary>
        public static event GameArchiveReceivedHandler GameArchiveReceived;

        /// <summary>
        /// The received game new content event handler.
        /// </summary>
        /// <param name="gameName">The game name that received new content</param>
        public delegate void GameNewContentReceivedHandler(string gameName);
        /// <summary>
        /// Fire when a game has received new content
        /// </summary>
        public static event GameNewContentReceivedHandler GameNewContentReceived;

        /// <summary>
        /// The received project list event handler.
        /// </summary>
        /// <param name="list">The list of all installed projects for the specified game</param>
        public delegate void ProjectListReceivedHandler(List<string> list);
        /// <summary>
        /// Fire when a project list is received
        /// </summary>
        public static event ProjectListReceivedHandler ProjectListReceived;

        /// <summary>
        /// The received basic project data event handler.
        /// </summary>
        /// <param name="description">The description of the project</param>
        public delegate void ProjectBasicDataReceivedHandler(string description);
        /// <summary>
        /// Fire when basic project data is received
        /// </summary>
        public static event ProjectBasicDataReceivedHandler ProjectBasicDataReceived;

        /// <summary>
        /// The received project event handler.
        /// </summary>
        /// <param name="XD">The project XML file</param>
        public delegate void ProjectReceivedHandler(XmlDocument XD);
        /// <summary>
        /// Fire when a project XML is received
        /// </summary>
        public static event ProjectReceivedHandler ProjectReceived;

        /// <summary>
        /// The received project archive event handler.
        /// </summary>
        /// <param name="data">The PPK archive of the project</param>
        public delegate void ProjectArchiveReceivedHandler(byte[] data);
        /// <summary>
        /// Fire when a project archive is received
        /// </summary>
        public static event ProjectArchiveReceivedHandler ProjectArchiveReceived;

        /// <summary>
        /// The project content changed event handler.
        /// </summary>
        /// <param name="name">The name of the project</param>
        /// <param name="gameName">The name of the game</param>
        public delegate void ProjectNewContentReceivedHandler(string name, string gameName);
        /// <summary>
        /// Fire when a project content is changed
        /// </summary>
        public static event ProjectNewContentReceivedHandler ProjectNewContentReceived;

        /// <summary>
        /// The template control list received event handler.
        /// </summary>
        /// <param name="list">The list of the names of the template controls</param>
        public delegate void TemplateCtrlListReceivedHandler(List<string> list);
        /// <summary>
        /// Fire when a list of control templates is received
        /// </summary>
        public static event TemplateCtrlListReceivedHandler TemplateCtrlListReceived;

        /// <summary>
        /// The template control basic infos received event handler.
        /// </summary>
        /// <param name="type">The type of control</param>
        /// <param name="description">The description of the template control</param>
        public delegate void TemplateCtrlBasicDataReceivedHandler(string type, string description);
        /// <summary>
        /// Fire when a basic information about a template control is received
        /// </summary>
        public static event TemplateCtrlBasicDataReceivedHandler TemplateCtrlBasicDataReceived;

        /// <summary>
        /// The template control XML received event handler.
        /// </summary>
        /// <param name="XD">The template control XML data</param>
        public delegate void TemplateCtrlReceivedHandler(XmlDocument XD);
        /// <summary>
        /// Fire when a template control XML data is received
        /// </summary>
        public static event TemplateCtrlReceivedHandler TemplateCtrlReceived;

        /// <summary>
        /// The template control archive received event handler. 
        /// </summary>
        /// <param name="data">The PPK archive of the template control</param>
        public delegate void TemplateCtrlArchiveReceivedHandler(byte[] data);
        /// <summary>
        /// Fire when a template control archive is received
        /// </summary>
        public static event TemplateCtrlArchiveReceivedHandler TemplateCtrlArchiveReceived;

        /// <summary>
        /// The template control new content received handler
        /// </summary>
        /// <param name="name">Tha name of the new or modified template control</param>
        /// <param name="gameName">The game name of the template control</param>
        public delegate void TemplateCtrlNewContentReceivedHandler(string name, string gameName);
        /// <summary>
        /// Fire when a template control content is modified or created
        /// </summary>
        public static event TemplateCtrlNewContentReceivedHandler TemplateCtrlNewContentReceived;

        /// <summary>
        /// The resource list received handler
        /// </summary>
        /// <param name="list">The list of resource in selected game</param>
        public delegate void ResourceListReceivedHandler(List<string> list);
        /// <summary>
        /// Fire when a list of resource is received
        /// </summary>
        public static event ResourceListReceivedHandler ResourceListReceived;

        /// <summary>
        /// The resource XML received handler
        /// </summary>
        /// <param name="XD">The XML of the resource</param>
        public delegate void ResourceReceivedHandler(XmlDocument XD);
        /// <summary>
        /// Fire when a resource XML data is received
        /// </summary>
        public static event ResourceReceivedHandler ResourceReceived;

        /// <summary>
        /// The resource archive received handler
        /// </summary>
        /// <param name="data"></param>
        public delegate void ResourceArchiveReceivedHandler(byte[] data);
        /// <summary>
        /// Fire when a resource archive is received
        /// </summary>
        public static event ResourceArchiveReceivedHandler ResourceArchiveReceived;

        /// <summary>
        /// The resource content changed handler
        /// </summary>
        /// <param name="name">The name of the created/modified resource</param>
        /// <param name="gameName">The game of the resource</param>
        public delegate void ResourceNewContentReceivedHandler(string name, string gameName);
        /// <summary>
        /// Fire when a resource content has been changed or created
        /// </summary>
        public static event ResourceNewContentReceivedHandler ResourceNewContentReceived;

        /// <summary>
        /// The enter edit mode handler
        /// </summary>
        public delegate void EditModeEnterHandler();
        /// <summary>
        /// Fire when remotes enter edit mode
        /// </summary>
        public static event EditModeEnterHandler EditModeEnter;

        /// <summary>
        /// The enter exit mode handler
        /// </summary>
        public delegate void EditModeExitHandler();
        /// <summary>
        /// Fire when remotes exit edit mode
        /// </summary>
        public static event EditModeExitHandler EditModeExit;

        static void localDecode(TCPLayerLite.dataBlock DB)
        {
            XmlDocument XD = new XmlDocument();
            try
            {
                using (MemoryStream MS = new MemoryStream(DB.data))
                {
                    using (StreamReader SR = new StreamReader(MS, Encoding.UTF8))
                    {
                        XD.Load(MS);
                    }
                }
            }
            catch (System.Exception ex)
            {
                Console.WriteLine("Incorrect data format. Error: " + ex.Message);
            }
            XmlNode root = XD.SelectSingleNode("command");
            if (root == null)
            {
                return;
            }
            XmlAttribute xa = root.Attributes["commandType"];
            if (xa == null)
            {
                return;
            }
            commandType CT;
            try
            {
                CT = (commandType)Convert.ToInt32(xa.Value, usCulture);
            }
            catch
            {
                return;
            }
            if (CT == commandType.GAME_LIST)
            {
                List<string> nameList = new List<string>(root.ChildNodes.Count);

                foreach (XmlNode child in root.ChildNodes)
                {
                    if (child.Name == "nameBlock")
                    {
                        xa = child.Attributes["name"];
                        if (xa != null)
                        {
                            nameList.Add(xa.Value);
                        }
                    }
                }

                if (GameListReceived != null)
                {
                    GameListReceived.Invoke(nameList);
                }
            }
            else if (CT == commandType.GAME_ARCHIVE_GET)
            {
                xa = root.Attributes["data"];
                if (xa == null)
                {
                    return;
                }
                byte[] data;
                try
                {
                    data = Convert.FromBase64String(xa.Value);
                }
                catch
                {
                    return;
                }

                if (GameArchiveReceived != null)
                {
                    GameArchiveReceived.Invoke(data);
                }
            }
            else if (CT == commandType.GAME_NEW_CONTENT)
            {
                xa = root.Attributes["gameName"];
                if (xa == null)
                {
                    return;
                }
                string gameName = xa.Value;

                if (GameNewContentReceived != null)
                {
                    GameNewContentReceived.Invoke(gameName);
                }
            }
            else if (CT == commandType.PROJECT_LIST)
            {
                List<string> nameList = new List<string>(root.ChildNodes.Count);

                foreach (XmlNode child in root.ChildNodes)
                {
                    if (child.Name == "nameBlock")
                    {
                        xa = child.Attributes["name"];
                        if (xa != null)
                        {
                            nameList.Add(xa.Value);
                        }
                    }
                }

                if (ProjectListReceived != null)
                {
                    ProjectListReceived.Invoke(nameList);
                }
            }
            else if (CT == commandType.PROJECT_GET_BASIC_DATA)
            {
                xa = root.Attributes["description"];
                if (xa == null)
                {
                    return;
                }
                string description = xa.Value;

                if (ProjectBasicDataReceived != null)
                {
                    ProjectBasicDataReceived.Invoke(description);
                }
            }
            else if (CT == commandType.PROJECT_UPLOAD)
            {
                XmlDocument projXD = new XmlDocument();
                projXD.AppendChild(projXD.CreateXmlDeclaration("1.0", "UTF-8", null));

                XmlNode prjNode = root.SelectSingleNode("template");
                if (prjNode != null)
                {
                    projXD.AppendChild(projXD.ImportNode(prjNode, true));

                    if (ProjectReceived != null)
                    {
                        ProjectReceived.Invoke(projXD);
                    }
                }
            }
            else if (CT == commandType.PROJECT_ARCHIVE_GET)
            {
                xa = root.Attributes["data"];
                if (xa == null)
                {
                    return;
                }
                byte[] data;
                try
                {
                    data = Convert.FromBase64String(xa.Value);
                }
                catch
                {
                    return;
                }

                if (ProjectArchiveReceived != null)
                {
                    ProjectArchiveReceived.Invoke(data);
                }
            }
            else if (CT == commandType.PROJECT_NEW_CONTENT)
            {
                xa = root.Attributes["name"];
                if (xa == null)
                {
                    return;
                }
                string name = xa.Value;

                xa = root.Attributes["gameName"];
                if (xa == null)
                {
                    return;
                }
                string gameName = xa.Value;

                if (ProjectNewContentReceived != null)
                {
                    ProjectNewContentReceived.Invoke(name, gameName);
                }
            }
            else if (CT == commandType.CTRL_TEMPLATE_LIST)
            {
                List<string> nameList = new List<string>(root.ChildNodes.Count);

                foreach (XmlNode child in root.ChildNodes)
                {
                    if (child.Name == "nameBlock")
                    {
                        xa = child.Attributes["name"];
                        if (xa != null)
                        {
                            nameList.Add(xa.Value);
                        }
                    }
                }

                if (TemplateCtrlListReceived != null)
                {
                    TemplateCtrlListReceived.Invoke(nameList);
                }
            }
            else if (CT == commandType.CTRL_TEMPLATE_GET_BASIC_DATA)
            {
                xa = root.Attributes["type"];
                if (xa == null)
                {
                    return;
                }
                string type = xa.Value;
                xa = root.Attributes["description"];
                if (xa == null)
                {
                    return;
                }
                string description = xa.Value;

                if (TemplateCtrlBasicDataReceived != null)
                {
                    TemplateCtrlBasicDataReceived.Invoke(type, description);
                }
            }
            else if (CT == commandType.CTRL_TEMPLATE_UPLOAD)
            {
                XmlDocument projXD = new XmlDocument();
                projXD.AppendChild(projXD.CreateXmlDeclaration("1.0", "UTF-8", null));

                XmlNode prjNode = root.SelectSingleNode("templateCtrl");
                if (prjNode != null)
                {
                    projXD.AppendChild(projXD.ImportNode(prjNode, true));

                    if (TemplateCtrlReceived != null)
                    {
                        TemplateCtrlReceived.Invoke(projXD);
                    }
                }
            }
            else if (CT == commandType.CTRL_TEMPLATE_ARCHIVE_GET)
            {
                xa = root.Attributes["data"];
                if (xa == null)
                {
                    return;
                }
                byte[] data;
                try
                {
                    data = Convert.FromBase64String(xa.Value);
                }
                catch
                {
                    return;
                }

                if (TemplateCtrlArchiveReceived != null)
                {
                    TemplateCtrlArchiveReceived.Invoke(data);
                }
            }
            else if (CT == commandType.CTRL_TEMPLATE_NEW_CONTENT)
            {
                xa = root.Attributes["name"];
                if (xa == null)
                {
                    return;
                }
                string name = xa.Value;

                xa = root.Attributes["gameName"];
                if (xa == null)
                {
                    return;
                }
                string gameName = xa.Value;

                if (TemplateCtrlNewContentReceived != null)
                {
                    TemplateCtrlNewContentReceived.Invoke(name, gameName);
                }
            }
            else if (CT == commandType.RESOURCE_LIST)
            {
                List<string> nameList = new List<string>(root.ChildNodes.Count);

                foreach (XmlNode child in root.ChildNodes)
                {
                    if (child.Name == "nameBlock")
                    {
                        xa = child.Attributes["name"];
                        if (xa != null)
                        {
                            nameList.Add(xa.Value);
                        }
                    }
                }

                if (ResourceListReceived != null)
                {
                    ResourceListReceived.Invoke(nameList);
                }
            }
            else if (CT == commandType.RESOURCE_UPLOAD)
            {
                XmlDocument projXD = new XmlDocument();
                projXD.AppendChild(projXD.CreateXmlDeclaration("1.0", "UTF-8", null));

                XmlNode prjNode = root.SelectSingleNode("resource");
                if (prjNode != null)
                {
                    projXD.AppendChild(projXD.ImportNode(prjNode, true));

                    if (ResourceReceived != null)
                    {
                        ResourceReceived.Invoke(projXD);
                    }
                }
            }
            else if (CT == commandType.RESOURCE_ARCHIVE_GET)
            {
                xa = root.Attributes["data"];
                if (xa == null)
                {
                    return;
                }
                byte[] data;
                try
                {
                    data = Convert.FromBase64String(xa.Value);
                }
                catch
                {
                    return;
                }

                if (ResourceArchiveReceived != null)
                {
                    ResourceArchiveReceived.Invoke(data);
                }
            }
            else if (CT == commandType.RESOURCE_NEW_CONTENT)
            {
                xa = root.Attributes["name"];
                if (xa == null)
                {
                    return;
                }
                string name = xa.Value;

                xa = root.Attributes["gameName"];
                if (xa == null)
                {
                    return;
                }
                string gameName = xa.Value;

                if (ResourceNewContentReceived != null)
                {
                    ResourceNewContentReceived.Invoke(name, gameName);
                }
            }
            else if (CT == commandType.EXTINPUT_PUSH_DATA)
            {
                List<EMData> EMDList = new List<EMData>(root.ChildNodes.Count);
                foreach (XmlNode xn in root.ChildNodes)
                {
                    EMData EMD = new EMData();
                    if (EMD.FromXml(xn))
                    {
                        EMDList.Add(EMD);
                    }
                }

                List<EMData> changeList = new List<EMData>(EMDList.Count);
                lock (commandStatus)
                {
                    foreach (EMData EMD in commandStatus) // Note that all non locally registered EMTags will be threw away
                    {
                        foreach (EMData newEMD in EMDList)
                        {
                            if (EMD.EMTag == newEMD.EMTag)
                            {
                                EMD.EMValue = newEMD.EMValue;
                                changeList.Add(newEMD);
                            }
                        }
                    }
                }

                if (changeList.Count > 0 && NewCommands != null)
                {
                    NewCommands.Invoke(changeList);
                }
            }
            else if (CT == commandType.EXTINPUT_PULL_DATA)
            {
                List<EMData> dataToSend;
                xa = root.Attributes["EMTags"];
                if (xa == null)
                {
                    lock (commandStatus)
                    {
                        dataToSend = new List<EMData>(commandStatus.Count);
                        foreach (EMData EMD in commandStatus)
                        {
                            dataToSend.Add(EMD.Copy());
                        }
                    }
                }
                else
                {
                    string[] allEMTags = xa.Value.Split(semicolon, StringSplitOptions.RemoveEmptyEntries);
                    lock (commandStatus)
                    {
                        dataToSend = new List<EMData>(commandStatus.Count);
                        foreach (EMData EMD in commandStatus)
                        {
                            if (allEMTags.Contains<string>(EMD.EMTag))
                            {
                                dataToSend.Add(EMD.Copy());
                            }
                        }
                    }
                }
                if (dataToSend.Count > 0)
                {
                    sendPushData(dataToSend);
                }
            }
            else if (CT == commandType.EXTINPUT_RESYNCH_DATA)
            {
                sendResynchData();
            }
            else if (CT == commandType.ENTER_EDIT_MODE)
            {
                if (EditModeEnter != null)
                {
                    EditModeEnter.Invoke();
                }
            }
            else if (CT == commandType.EXIT_EDIT_MODE)
            {
                if (EditModeExit != null)
                {
                    EditModeExit.Invoke();
                }
            }
        }

        // send commands

        /// <summary>
        /// Get list of game to server
        /// </summary>
        public static void sendGetGames()
        {
            XmlDocument XD = new XmlDocument();
            XD.AppendChild(XD.CreateXmlDeclaration("1.0", "UTF-8", null));

            XmlNode root = XD.CreateElement("command");
            XD.AppendChild(root);

            XmlAttribute xa = XD.CreateAttribute("commandType");
            xa.Value = ((int)commands.commandType.GAME_LIST).ToString("D", usCulture);
            root.Attributes.Append(xa);

            byte[] data;
            using (MemoryStream MS = new MemoryStream())
            {
                XmlWriterSettings settings = new XmlWriterSettings();
                settings.Encoding = System.Text.Encoding.UTF8;
                using (XmlWriter XW = XmlWriter.Create(MS, settings))
                {
                    XD.Save(XW);
                    data = MS.ToArray();
                }
            }
            TCPLayerLite.enqueueDataToSend(data, TCPLayerLite.deviceType.SERVER);
        }

        /// <summary>
        /// Create a new empty game on server
        /// </summary>
        /// <param name="gameName">The game name</param>
        public static void sendNewGame(string gameName)
        {
            XmlDocument XD = new XmlDocument();
            XD.AppendChild(XD.CreateXmlDeclaration("1.0", "UTF-8", null));

            XmlNode root = XD.CreateElement("command");
            XD.AppendChild(root);

            XmlAttribute xa = XD.CreateAttribute("commandType");
            xa.Value = ((int)commands.commandType.GAME_NEW).ToString("D", usCulture);
            root.Attributes.Append(xa);

            xa = XD.CreateAttribute("gameName");
            xa.Value = gameName;
            root.Attributes.Append(xa);

            byte[] data;
            using (MemoryStream MS = new MemoryStream())
            {
                XmlWriterSettings settings = new XmlWriterSettings();
                settings.Encoding = System.Text.Encoding.UTF8;
                using (XmlWriter XW = XmlWriter.Create(MS, settings))
                {
                    XD.Save(XW);
                    data = MS.ToArray();
                }
            }
            TCPLayerLite.enqueueDataToSend(data, TCPLayerLite.deviceType.SERVER);
        }

        /// <summary>
        /// Copy a game content on server
        /// </summary>
        /// <param name="newGameName">The new game name</param>
        /// <param name="gameName">The game to copy</param>
        public static void sendCopyGame(string newGameName, string gameName)
        {
            XmlDocument XD = new XmlDocument();
            XD.AppendChild(XD.CreateXmlDeclaration("1.0", "UTF-8", null));

            XmlNode root = XD.CreateElement("command");
            XD.AppendChild(root);

            XmlAttribute xa = XD.CreateAttribute("commandType");
            xa.Value = ((int)commands.commandType.GAME_COPY).ToString("D", usCulture);
            root.Attributes.Append(xa);

            xa = XD.CreateAttribute("gameName");
            xa.Value = gameName;
            root.Attributes.Append(xa);

            xa = XD.CreateAttribute("newGameName");
            xa.Value = newGameName;
            root.Attributes.Append(xa);

            byte[] data;
            using (MemoryStream MS = new MemoryStream())
            {
                XmlWriterSettings settings = new XmlWriterSettings();
                settings.Encoding = System.Text.Encoding.UTF8;
                using (XmlWriter XW = XmlWriter.Create(MS, settings))
                {
                    XD.Save(XW);
                    data = MS.ToArray();
                }
            }
            TCPLayerLite.enqueueDataToSend(data, TCPLayerLite.deviceType.SERVER);
        }

        /// <summary>
        /// Rename a game on server
        /// </summary>
        /// <param name="newGameName">The new game name</param>
        /// <param name="gameName">The game to rename</param>
        public static void sendRenameGame(string newGameName, string gameName)
        {
            XmlDocument XD = new XmlDocument();
            XD.AppendChild(XD.CreateXmlDeclaration("1.0", "UTF-8", null));

            XmlNode root = XD.CreateElement("command");
            XD.AppendChild(root);

            XmlAttribute xa = XD.CreateAttribute("commandType");
            xa.Value = ((int)commands.commandType.GAME_RENAME).ToString("D", usCulture);
            root.Attributes.Append(xa);

            xa = XD.CreateAttribute("gameName");
            xa.Value = gameName;
            root.Attributes.Append(xa);

            xa = XD.CreateAttribute("newGameName");
            xa.Value = newGameName;
            root.Attributes.Append(xa);

            byte[] data;
            using (MemoryStream MS = new MemoryStream())
            {
                XmlWriterSettings settings = new XmlWriterSettings();
                settings.Encoding = System.Text.Encoding.UTF8;
                using (XmlWriter XW = XmlWriter.Create(MS, settings))
                {
                    XD.Save(XW);
                    data = MS.ToArray();
                }
            }
            TCPLayerLite.enqueueDataToSend(data, TCPLayerLite.deviceType.SERVER);
        }

        /// <summary>
        /// Delete specified game on server
        /// </summary>
        /// <param name="gameName">The game to delete</param>
        public static void sendDeleteGame(string gameName)
        {
            XmlDocument XD = new XmlDocument();
            XD.AppendChild(XD.CreateXmlDeclaration("1.0", "UTF-8", null));

            XmlNode root = XD.CreateElement("command");
            XD.AppendChild(root);

            XmlAttribute xa = XD.CreateAttribute("commandType");
            xa.Value = ((int)commands.commandType.GAME_DELETE).ToString("D", usCulture);
            root.Attributes.Append(xa);

            xa = XD.CreateAttribute("gameName");
            xa.Value = gameName;
            root.Attributes.Append(xa);

            byte[] data;
            using (MemoryStream MS = new MemoryStream())
            {
                XmlWriterSettings settings = new XmlWriterSettings();
                settings.Encoding = System.Text.Encoding.UTF8;
                using (XmlWriter XW = XmlWriter.Create(MS, settings))
                {
                    XD.Save(XW);
                    data = MS.ToArray();
                }
            }
            TCPLayerLite.enqueueDataToSend(data, TCPLayerLite.deviceType.SERVER);
        }

        /// <summary>
        /// Change game description
        /// </summary>
        /// <param name="gameName">The game to update</param>
        /// <param name="description">The new description</param>
        public static void sendChangeDescriptionGame(string gameName, string description)
        {
            XmlDocument XD = new XmlDocument();
            XD.AppendChild(XD.CreateXmlDeclaration("1.0", "UTF-8", null));

            XmlNode root = XD.CreateElement("command");
            XD.AppendChild(root);

            XmlAttribute xa = XD.CreateAttribute("commandType");
            xa.Value = ((int)commands.commandType.GAME_SET_DESCRIPTION).ToString("D", usCulture);
            root.Attributes.Append(xa);

            xa = XD.CreateAttribute("gameName");
            xa.Value = gameName;
            root.Attributes.Append(xa);

            xa = XD.CreateAttribute("description");
            xa.Value = description;
            root.Attributes.Append(xa);

            byte[] data;
            using (MemoryStream MS = new MemoryStream())
            {
                XmlWriterSettings settings = new XmlWriterSettings();
                settings.Encoding = System.Text.Encoding.UTF8;
                using (XmlWriter XW = XmlWriter.Create(MS, settings))
                {
                    XD.Save(XW);
                    data = MS.ToArray();
                }
            }
            TCPLayerLite.enqueueDataToSend(data, TCPLayerLite.deviceType.SERVER);
        }

        /// <summary>
        /// Get the projects names of specified game
        /// </summary>
        /// <param name="gameName">The game name</param>
        public static void sendGetProjectNames(string gameName)
        {
            XmlDocument XD = new XmlDocument();
            XD.AppendChild(XD.CreateXmlDeclaration("1.0", "UTF-8", null));

            XmlNode root = XD.CreateElement("command");
            XD.AppendChild(root);

            XmlAttribute xa = XD.CreateAttribute("commandType");
            xa.Value = ((int)commands.commandType.PROJECT_LIST).ToString("D", usCulture);
            root.Attributes.Append(xa);

            xa = XD.CreateAttribute("gameName");
            xa.Value = gameName;
            root.Attributes.Append(xa);

            byte[] data;
            using (MemoryStream MS = new MemoryStream())
            {
                XmlWriterSettings settings = new XmlWriterSettings();
                settings.Encoding = System.Text.Encoding.UTF8;
                using (XmlWriter XW = XmlWriter.Create(MS, settings))
                {
                    XD.Save(XW);
                    data = MS.ToArray();
                }
            }
            TCPLayerLite.enqueueDataToSend(data, TCPLayerLite.deviceType.SERVER);
        }

        /// <summary>
        /// Get the specified project XML
        /// </summary>
        /// <param name="name">The name of the project</param>
        /// <param name="gameName">The game name</param>
        public static void sendGetProject(string name, string gameName)
        {
            XmlDocument XD = new XmlDocument();
            XD.AppendChild(XD.CreateXmlDeclaration("1.0", "UTF-8", null));

            XmlNode root = XD.CreateElement("command");
            XD.AppendChild(root);

            XmlAttribute xa = XD.CreateAttribute("commandType");
            xa.Value = ((int)commands.commandType.PROJECT_DOWNLOAD).ToString("D", usCulture);
            root.Attributes.Append(xa);

            xa = XD.CreateAttribute("name");
            xa.Value = name;
            root.Attributes.Append(xa);

            xa = XD.CreateAttribute("gameName");
            xa.Value = gameName;
            root.Attributes.Append(xa);

            byte[] data;
            using (MemoryStream MS = new MemoryStream())
            {
                XmlWriterSettings settings = new XmlWriterSettings();
                settings.Encoding = System.Text.Encoding.UTF8;
                using (XmlWriter XW = XmlWriter.Create(MS, settings))
                {
                    XD.Save(XW);
                    data = MS.ToArray();
                }
            }
            TCPLayerLite.enqueueDataToSend(data, TCPLayerLite.deviceType.SERVER);
        }

        /// <summary>
        /// Save the specified project XML. The project XML gameName attribute must contains the destination game name.
        /// </summary>
        /// <param name="prj">The XML data</param>
        public static void sendSaveProject(XmlDocument prj)
        {
            XmlDocument XD = new XmlDocument();
            XD.AppendChild(XD.CreateXmlDeclaration("1.0", "UTF-8", null));

            XmlNode root = XD.CreateElement("command");
            XD.AppendChild(root);

            XmlAttribute xa = XD.CreateAttribute("commandType");
            xa.Value = ((int)commands.commandType.PROJECT_UPLOAD).ToString("D", usCulture);
            root.Attributes.Append(xa);

            XmlNode tempNode = prj.SelectSingleNode("template");
            if (tempNode == null)
            {
                return;
            }
            XmlNode impNode = XD.ImportNode(tempNode, true);
            root.AppendChild(impNode);

            byte[] data;
            using (MemoryStream MS = new MemoryStream())
            {
                XmlWriterSettings settings = new XmlWriterSettings();
                settings.Encoding = System.Text.Encoding.UTF8;
                using (XmlWriter XW = XmlWriter.Create(MS, settings))
                {
                    XD.Save(XW);
                    data = MS.ToArray();
                }
            }
            TCPLayerLite.enqueueDataToSend(data, TCPLayerLite.deviceType.SERVER);
        }

        /// <summary>
        /// Delete the specified project
        /// </summary>
        /// <param name="name">The name of the project</param>
        /// <param name="gameName">The game name</param>
        public static void sendDeleteProject(string name, string gameName)
        {
            XmlDocument XD = new XmlDocument();
            XD.AppendChild(XD.CreateXmlDeclaration("1.0", "UTF-8", null));

            XmlNode root = XD.CreateElement("command");
            XD.AppendChild(root);

            XmlAttribute xa = XD.CreateAttribute("commandType");
            xa.Value = ((int)commands.commandType.PROJECT_DELETE).ToString("D", usCulture);
            root.Attributes.Append(xa);

            xa = XD.CreateAttribute("name");
            xa.Value = name;
            root.Attributes.Append(xa);

            xa = XD.CreateAttribute("gameName");
            xa.Value = gameName;
            root.Attributes.Append(xa);

            byte[] data;
            using (MemoryStream MS = new MemoryStream())
            {
                XmlWriterSettings settings = new XmlWriterSettings();
                settings.Encoding = System.Text.Encoding.UTF8;
                using (XmlWriter XW = XmlWriter.Create(MS, settings))
                {
                    XD.Save(XW);
                    data = MS.ToArray();
                }
            }
            TCPLayerLite.enqueueDataToSend(data, TCPLayerLite.deviceType.SERVER);
        }

        /// <summary>
        /// Copy the specified project
        /// </summary>
        /// <param name="newName">The new project name</param>
        /// <param name="name">The project to copy</param>
        /// <param name="gameName">The game name</param>
        public static void sendCopyProject(string newName, string name, string gameName)
        {
            XmlDocument XD = new XmlDocument();
            XD.AppendChild(XD.CreateXmlDeclaration("1.0", "UTF-8", null));

            XmlNode root = XD.CreateElement("command");
            XD.AppendChild(root);

            XmlAttribute xa = XD.CreateAttribute("commandType");
            xa.Value = ((int)commands.commandType.PROJECT_COPY).ToString("D", usCulture);
            root.Attributes.Append(xa);

            xa = XD.CreateAttribute("newName");
            xa.Value = newName;
            root.Attributes.Append(xa);

            xa = XD.CreateAttribute("name");
            xa.Value = name;
            root.Attributes.Append(xa);

            xa = XD.CreateAttribute("gameName");
            xa.Value = gameName;
            root.Attributes.Append(xa);

            byte[] data;
            using (MemoryStream MS = new MemoryStream())
            {
                XmlWriterSettings settings = new XmlWriterSettings();
                settings.Encoding = System.Text.Encoding.UTF8;
                using (XmlWriter XW = XmlWriter.Create(MS, settings))
                {
                    XD.Save(XW);
                    data = MS.ToArray();
                }
            }
            TCPLayerLite.enqueueDataToSend(data, TCPLayerLite.deviceType.SERVER);
        }

        /// <summary>
        /// Rename the specified project
        /// </summary>
        /// <param name="newName">The new project name</param>
        /// <param name="name">The project to rename</param>
        /// <param name="gameName">The game name</param>
        public static void sendRenameProject(string newName, string name, string gameName)
        {
            XmlDocument XD = new XmlDocument();
            XD.AppendChild(XD.CreateXmlDeclaration("1.0", "UTF-8", null));

            XmlNode root = XD.CreateElement("command");
            XD.AppendChild(root);

            XmlAttribute xa = XD.CreateAttribute("commandType");
            xa.Value = ((int)commands.commandType.PROJECT_RENAME).ToString("D", usCulture);
            root.Attributes.Append(xa);

            xa = XD.CreateAttribute("newName");
            xa.Value = newName;
            root.Attributes.Append(xa);

            xa = XD.CreateAttribute("name");
            xa.Value = name;
            root.Attributes.Append(xa);

            xa = XD.CreateAttribute("gameName");
            xa.Value = gameName;
            root.Attributes.Append(xa);

            byte[] data;
            using (MemoryStream MS = new MemoryStream())
            {
                XmlWriterSettings settings = new XmlWriterSettings();
                settings.Encoding = System.Text.Encoding.UTF8;
                using (XmlWriter XW = XmlWriter.Create(MS, settings))
                {
                    XD.Save(XW);
                    data = MS.ToArray();
                }
            }
            TCPLayerLite.enqueueDataToSend(data, TCPLayerLite.deviceType.SERVER);
        }

        /// <summary>
        /// Get basic data of project
        /// </summary>
        /// <param name="projectName">The project name</param>
        /// <param name="gameName">The game name</param>
        public static void sendGetBasicDataProject(string projectName, string gameName)
        {
            XmlDocument XD = new XmlDocument();
            XD.AppendChild(XD.CreateXmlDeclaration("1.0", "UTF-8", null));

            XmlNode root = XD.CreateElement("command");
            XD.AppendChild(root);

            XmlAttribute xa = XD.CreateAttribute("commandType");
            xa.Value = ((int)commands.commandType.PROJECT_GET_BASIC_DATA).ToString("D", usCulture);
            root.Attributes.Append(xa);

            xa = XD.CreateAttribute("name");
            xa.Value = projectName;
            root.Attributes.Append(xa);

            xa = XD.CreateAttribute("gameName");
            xa.Value = gameName;
            root.Attributes.Append(xa);

            byte[] data;
            using (MemoryStream MS = new MemoryStream())
            {
                XmlWriterSettings settings = new XmlWriterSettings();
                settings.Encoding = System.Text.Encoding.UTF8;
                using (XmlWriter XW = XmlWriter.Create(MS, settings))
                {
                    XD.Save(XW);
                    data = MS.ToArray();
                }
            }
            TCPLayerLite.enqueueDataToSend(data, TCPLayerLite.deviceType.SERVER);
        }

        /// <summary>
        /// Change the description of project
        /// </summary>
        /// <param name="name">The name of the project</param>
        /// <param name="gameName">The game name</param>
        /// <param name="description">The new description</param>
        public static void sendChangeDescriptionProject(string name, string gameName, string description)
        {
            XmlDocument XD = new XmlDocument();
            XD.AppendChild(XD.CreateXmlDeclaration("1.0", "UTF-8", null));

            XmlNode root = XD.CreateElement("command");
            XD.AppendChild(root);

            XmlAttribute xa = XD.CreateAttribute("commandType");
            xa.Value = ((int)commands.commandType.PROJECT_SET_DESCRIPTION).ToString("D", usCulture);
            root.Attributes.Append(xa);

            xa = XD.CreateAttribute("name");
            xa.Value = name;
            root.Attributes.Append(xa);

            xa = XD.CreateAttribute("gameName");
            xa.Value = gameName;
            root.Attributes.Append(xa);

            xa = XD.CreateAttribute("description");
            xa.Value = description;
            root.Attributes.Append(xa);

            byte[] data;
            using (MemoryStream MS = new MemoryStream())
            {
                XmlWriterSettings settings = new XmlWriterSettings();
                settings.Encoding = System.Text.Encoding.UTF8;
                using (XmlWriter XW = XmlWriter.Create(MS, settings))
                {
                    XD.Save(XW);
                    data = MS.ToArray();
                }
            }
            TCPLayerLite.enqueueDataToSend(data, TCPLayerLite.deviceType.SERVER);
        }

        /// <summary>
        /// Get all template controls of the specified game
        /// </summary>
        /// <param name="gameName">The game name</param>
        public static void sendGetCtrlTemplates(string gameName)
        {
            XmlDocument XD = new XmlDocument();
            XD.AppendChild(XD.CreateXmlDeclaration("1.0", "UTF-8", null));

            XmlNode root = XD.CreateElement("command");
            XD.AppendChild(root);

            XmlAttribute xa = XD.CreateAttribute("commandType");
            xa.Value = ((int)commands.commandType.CTRL_TEMPLATE_DOWNLOAD_ALL).ToString("D", usCulture);
            root.Attributes.Append(xa);

            xa = XD.CreateAttribute("gameName");
            xa.Value = gameName;
            root.Attributes.Append(xa);

            byte[] data;
            using (MemoryStream MS = new MemoryStream())
            {
                XmlWriterSettings settings = new XmlWriterSettings();
                settings.Encoding = System.Text.Encoding.UTF8;
                using (XmlWriter XW = XmlWriter.Create(MS, settings))
                {
                    XD.Save(XW);
                    data = MS.ToArray();
                }
            }
            TCPLayerLite.enqueueDataToSend(data, TCPLayerLite.deviceType.SERVER);
        }

        /// <summary>
        /// Get all template controls names of the specified game
        /// </summary>
        /// <param name="gameName">The game name</param>
        public static void sendGetCtrlTemplateNames(string gameName)
        {
            XmlDocument XD = new XmlDocument();
            XD.AppendChild(XD.CreateXmlDeclaration("1.0", "UTF-8", null));

            XmlNode root = XD.CreateElement("command");
            XD.AppendChild(root);

            XmlAttribute xa = XD.CreateAttribute("commandType");
            xa.Value = ((int)commands.commandType.CTRL_TEMPLATE_LIST).ToString("D", usCulture);
            root.Attributes.Append(xa);

            xa = XD.CreateAttribute("gameName");
            xa.Value = gameName;
            root.Attributes.Append(xa);

            byte[] data;
            using (MemoryStream MS = new MemoryStream())
            {
                XmlWriterSettings settings = new XmlWriterSettings();
                settings.Encoding = System.Text.Encoding.UTF8;
                using (XmlWriter XW = XmlWriter.Create(MS, settings))
                {
                    XD.Save(XW);
                    data = MS.ToArray();
                }
            }
            TCPLayerLite.enqueueDataToSend(data, TCPLayerLite.deviceType.SERVER);
        }

        /// <summary>
        /// Save template control XML. The template control XML gameName attribute must contains the destination game name.
        /// </summary>
        /// <param name="temp"></param>
        public static void sendSaveCtrlTemplate(XmlDocument temp)
        {
            XmlDocument XD = new XmlDocument();
            XD.AppendChild(XD.CreateXmlDeclaration("1.0", "UTF-8", null));

            XmlNode root = XD.CreateElement("command");
            XD.AppendChild(root);

            XmlAttribute xa = XD.CreateAttribute("commandType");
            xa.Value = ((int)commands.commandType.CTRL_TEMPLATE_UPLOAD).ToString("D", usCulture);
            root.Attributes.Append(xa);

            XmlNode tempNode = temp.SelectSingleNode("templateCtrl");
            if (tempNode == null)
            {
                return;
            }
            XmlNode impNode = XD.ImportNode(tempNode, true);
            root.AppendChild(impNode);

            byte[] data;
            using (MemoryStream MS = new MemoryStream())
            {
                XmlWriterSettings settings = new XmlWriterSettings();
                settings.Encoding = System.Text.Encoding.UTF8;
                using (XmlWriter XW = XmlWriter.Create(MS, settings))
                {
                    XD.Save(XW);
                    data = MS.ToArray();
                }
            }
            TCPLayerLite.enqueueDataToSend(data, TCPLayerLite.deviceType.SERVER);
        }

        /// <summary>
        /// Delete specified template control
        /// </summary>
        /// <param name="templateName">The name of the template</param>
        /// <param name="gameName">The game name</param>
        public static void sendDeleteCtrlTemplate(string templateName, string gameName)
        {
            XmlDocument XD = new XmlDocument();
            XD.AppendChild(XD.CreateXmlDeclaration("1.0", "UTF-8", null));

            XmlNode root = XD.CreateElement("command");
            XD.AppendChild(root);

            XmlAttribute xa = XD.CreateAttribute("commandType");
            xa.Value = ((int)commands.commandType.CTRL_TEMPLATE_DELETE).ToString("D", usCulture);
            root.Attributes.Append(xa);

            xa = XD.CreateAttribute("name");
            xa.Value = templateName;
            root.Attributes.Append(xa);

            xa = XD.CreateAttribute("gameName");
            xa.Value = gameName;
            root.Attributes.Append(xa);

            byte[] data;
            using (MemoryStream MS = new MemoryStream())
            {
                XmlWriterSettings settings = new XmlWriterSettings();
                settings.Encoding = System.Text.Encoding.UTF8;
                using (XmlWriter XW = XmlWriter.Create(MS, settings))
                {
                    XD.Save(XW);
                    data = MS.ToArray();
                }
            }
            TCPLayerLite.enqueueDataToSend(data, TCPLayerLite.deviceType.SERVER);
        }

        /// <summary>
        /// Copy the specified template control
        /// </summary>
        /// <param name="newName">The new template control name</param>
        /// <param name="name">The template control to copy</param>
        /// <param name="gameName">The game name</param>
        public static void sendCopyCtrlTemplate(string newName, string name, string gameName)
        {
            XmlDocument XD = new XmlDocument();
            XD.AppendChild(XD.CreateXmlDeclaration("1.0", "UTF-8", null));

            XmlNode root = XD.CreateElement("command");
            XD.AppendChild(root);

            XmlAttribute xa = XD.CreateAttribute("commandType");
            xa.Value = ((int)commands.commandType.CTRL_TEMPLATE_COPY).ToString("D", usCulture);
            root.Attributes.Append(xa);

            xa = XD.CreateAttribute("newName");
            xa.Value = newName;
            root.Attributes.Append(xa);

            xa = XD.CreateAttribute("name");
            xa.Value = name;
            root.Attributes.Append(xa);

            xa = XD.CreateAttribute("gameName");
            xa.Value = gameName;
            root.Attributes.Append(xa);

            byte[] data;
            using (MemoryStream MS = new MemoryStream())
            {
                XmlWriterSettings settings = new XmlWriterSettings();
                settings.Encoding = System.Text.Encoding.UTF8;
                using (XmlWriter XW = XmlWriter.Create(MS, settings))
                {
                    XD.Save(XW);
                    data = MS.ToArray();
                }
            }
            TCPLayerLite.enqueueDataToSend(data, TCPLayerLite.deviceType.SERVER);
        }

        /// <summary>
        /// Rename the specified template control
        /// </summary>
        /// <param name="newName">The new template control name</param>
        /// <param name="name">The template control to rename</param>
        /// <param name="gameName">The gama name</param>
        public static void sendRenameCtrlTemplate(string newName, string name, string gameName)
        {
            XmlDocument XD = new XmlDocument();
            XD.AppendChild(XD.CreateXmlDeclaration("1.0", "UTF-8", null));

            XmlNode root = XD.CreateElement("command");
            XD.AppendChild(root);

            XmlAttribute xa = XD.CreateAttribute("commandType");
            xa.Value = ((int)commands.commandType.CTRL_TEMPLATE_RENAME).ToString("D", usCulture);
            root.Attributes.Append(xa);

            xa = XD.CreateAttribute("newName");
            xa.Value = newName;
            root.Attributes.Append(xa);

            xa = XD.CreateAttribute("name");
            xa.Value = name;
            root.Attributes.Append(xa);

            xa = XD.CreateAttribute("gameName");
            xa.Value = gameName;
            root.Attributes.Append(xa);

            byte[] data;
            using (MemoryStream MS = new MemoryStream())
            {
                XmlWriterSettings settings = new XmlWriterSettings();
                settings.Encoding = System.Text.Encoding.UTF8;
                using (XmlWriter XW = XmlWriter.Create(MS, settings))
                {
                    XD.Save(XW);
                    data = MS.ToArray();
                }
            }
            TCPLayerLite.enqueueDataToSend(data, TCPLayerLite.deviceType.SERVER);
        }

        /// <summary>
        /// Get the template control basic data
        /// </summary>
        /// <param name="templateName">The template name</param>
        /// <param name="gameName">The game name</param>
        public static void sendGetBasicDataCtrlTemplate(string templateName, string gameName)
        {
            XmlDocument XD = new XmlDocument();
            XD.AppendChild(XD.CreateXmlDeclaration("1.0", "UTF-8", null));

            XmlNode root = XD.CreateElement("command");
            XD.AppendChild(root);

            XmlAttribute xa = XD.CreateAttribute("commandType");
            xa.Value = ((int)commands.commandType.CTRL_TEMPLATE_GET_BASIC_DATA).ToString("D", usCulture);
            root.Attributes.Append(xa);

            xa = XD.CreateAttribute("name");
            xa.Value = templateName;
            root.Attributes.Append(xa);

            xa = XD.CreateAttribute("gameName");
            xa.Value = gameName;
            root.Attributes.Append(xa);

            byte[] data;
            using (MemoryStream MS = new MemoryStream())
            {
                XmlWriterSettings settings = new XmlWriterSettings();
                settings.Encoding = System.Text.Encoding.UTF8;
                using (XmlWriter XW = XmlWriter.Create(MS, settings))
                {
                    XD.Save(XW);
                    data = MS.ToArray();
                }
            }
            TCPLayerLite.enqueueDataToSend(data, TCPLayerLite.deviceType.SERVER);
        }

        /// <summary>
        /// Change description template control
        /// </summary>
        /// <param name="templateName">The template name</param>
        /// <param name="gameName">The game name</param>
        /// <param name="description">The new description</param>
        public static void sendChangeDescriptionCtrlTemplate(string templateName, string gameName, string description)
        {
            XmlDocument XD = new XmlDocument();
            XD.AppendChild(XD.CreateXmlDeclaration("1.0", "UTF-8", null));

            XmlNode root = XD.CreateElement("command");
            XD.AppendChild(root);

            XmlAttribute xa = XD.CreateAttribute("commandType");
            xa.Value = ((int)commands.commandType.CTRL_TEMPLATE_SET_DESCRIPTION).ToString("D", usCulture);
            root.Attributes.Append(xa);

            xa = XD.CreateAttribute("name");
            xa.Value = templateName;
            root.Attributes.Append(xa);

            xa = XD.CreateAttribute("gameName");
            xa.Value = gameName;
            root.Attributes.Append(xa);

            xa = XD.CreateAttribute("description");
            xa.Value = description;
            root.Attributes.Append(xa);

            byte[] data;
            using (MemoryStream MS = new MemoryStream())
            {
                XmlWriterSettings settings = new XmlWriterSettings();
                settings.Encoding = System.Text.Encoding.UTF8;
                using (XmlWriter XW = XmlWriter.Create(MS, settings))
                {
                    XD.Save(XW);
                    data = MS.ToArray();
                }
            }
            TCPLayerLite.enqueueDataToSend(data, TCPLayerLite.deviceType.SERVER);
        }

        /// <summary>
        /// Get all the resources name of specified game
        /// </summary>
        /// <param name="gameName">The game name</param>
        public static void sendGetResourceNames(string gameName)
        {
            XmlDocument XD = new XmlDocument();
            XD.AppendChild(XD.CreateXmlDeclaration("1.0", "UTF-8", null));

            XmlNode root = XD.CreateElement("command");
            XD.AppendChild(root);

            XmlAttribute xa = XD.CreateAttribute("commandType");
            xa.Value = ((int)commands.commandType.RESOURCE_LIST).ToString("D", usCulture);
            root.Attributes.Append(xa);

            xa = XD.CreateAttribute("gameName");
            xa.Value = gameName;
            root.Attributes.Append(xa);

            byte[] data;
            using (MemoryStream MS = new MemoryStream())
            {
                XmlWriterSettings settings = new XmlWriterSettings();
                settings.Encoding = System.Text.Encoding.UTF8;
                using (XmlWriter XW = XmlWriter.Create(MS, settings))
                {
                    XD.Save(XW);
                    data = MS.ToArray();
                }
            }
            TCPLayerLite.enqueueDataToSend(data, TCPLayerLite.deviceType.SERVER);
        }

        /// <summary>
        /// Get all the resources of specified game
        /// </summary>
        /// <param name="gameName"></param>
        public static void sendGetResources(string gameName)
        {
            XmlDocument XD = new XmlDocument();
            XD.AppendChild(XD.CreateXmlDeclaration("1.0", "UTF-8", null));

            XmlNode root = XD.CreateElement("command");
            XD.AppendChild(root);

            XmlAttribute xa = XD.CreateAttribute("commandType");
            xa.Value = ((int)commands.commandType.RESOURCE_DOWNLOAD_ALL).ToString("D", usCulture);
            root.Attributes.Append(xa);

            xa = XD.CreateAttribute("gameName");
            xa.Value = gameName;
            root.Attributes.Append(xa);

            byte[] data;
            using (MemoryStream MS = new MemoryStream())
            {
                XmlWriterSettings settings = new XmlWriterSettings();
                settings.Encoding = System.Text.Encoding.UTF8;
                using (XmlWriter XW = XmlWriter.Create(MS, settings))
                {
                    XD.Save(XW);
                    data = MS.ToArray();
                }
            }
            TCPLayerLite.enqueueDataToSend(data, TCPLayerLite.deviceType.SERVER);
        }

        /// <summary>
        /// Get the specified resource
        /// </summary>
        /// <param name="name">The resource name</param>
        /// <param name="gameName">The game name</param>
        public static void sendGetResource(string name, string gameName)
        {
            XmlDocument XD = new XmlDocument();
            XD.AppendChild(XD.CreateXmlDeclaration("1.0", "UTF-8", null));

            XmlNode root = XD.CreateElement("command");
            XD.AppendChild(root);

            XmlAttribute xa = XD.CreateAttribute("commandType");
            xa.Value = ((int)commands.commandType.RESOURCE_DOWNLOAD).ToString("D", usCulture);
            root.Attributes.Append(xa);

            xa = XD.CreateAttribute("name");
            xa.Value = name;
            root.Attributes.Append(xa);

            xa = XD.CreateAttribute("gameName");
            xa.Value = gameName;
            root.Attributes.Append(xa);

            byte[] data;
            using (MemoryStream MS = new MemoryStream())
            {
                XmlWriterSettings settings = new XmlWriterSettings();
                settings.Encoding = System.Text.Encoding.UTF8;
                using (XmlWriter XW = XmlWriter.Create(MS, settings))
                {
                    XD.Save(XW);
                    data = MS.ToArray();
                }
            }
            TCPLayerLite.enqueueDataToSend(data, TCPLayerLite.deviceType.SERVER);
        }

        /// <summary>
        /// Save resource
        /// </summary>
        /// <param name="res">the resource XML</param>
        public static void sendSaveResource(XmlDocument res)
        {
            XmlDocument XD = new XmlDocument();
            XD.AppendChild(XD.CreateXmlDeclaration("1.0", "UTF-8", null));

            XmlNode root = XD.CreateElement("command");
            XD.AppendChild(root);

            XmlAttribute xa = XD.CreateAttribute("commandType");
            xa.Value = ((int)commands.commandType.RESOURCE_UPLOAD).ToString("D", usCulture);
            root.Attributes.Append(xa);

            XmlNode tempNode = res.SelectSingleNode("resource");
            if (tempNode == null)
            {
                return;
            }
            XmlNode impNode = XD.ImportNode(tempNode, true);
            root.AppendChild(impNode);

            byte[] data;
            using (MemoryStream MS = new MemoryStream())
            {
                XmlWriterSettings settings = new XmlWriterSettings();
                settings.Encoding = System.Text.Encoding.UTF8;
                using (XmlWriter XW = XmlWriter.Create(MS, settings))
                {
                    XD.Save(XW);
                    data = MS.ToArray();
                }
            }
            TCPLayerLite.enqueueDataToSend(data, TCPLayerLite.deviceType.SERVER);
        }

        /// <summary>
        /// Delete the specified resource
        /// </summary>
        /// <param name="resourceName">The resource name</param>
        /// <param name="gameName">The game name</param>
        public static void sendDeleteResource(string resourceName, string gameName)
        {
            XmlDocument XD = new XmlDocument();
            XD.AppendChild(XD.CreateXmlDeclaration("1.0", "UTF-8", null));

            XmlNode root = XD.CreateElement("command");
            XD.AppendChild(root);

            XmlAttribute xa = XD.CreateAttribute("commandType");
            xa.Value = ((int)commands.commandType.RESOURCE_DELETE).ToString("D", usCulture);
            root.Attributes.Append(xa);

            xa = XD.CreateAttribute("name");
            xa.Value = resourceName;
            root.Attributes.Append(xa);

            xa = XD.CreateAttribute("gameName");
            xa.Value = gameName;
            root.Attributes.Append(xa);

            byte[] data;
            using (MemoryStream MS = new MemoryStream())
            {
                XmlWriterSettings settings = new XmlWriterSettings();
                settings.Encoding = System.Text.Encoding.UTF8;
                using (XmlWriter XW = XmlWriter.Create(MS, settings))
                {
                    XD.Save(XW);
                    data = MS.ToArray();
                }
            }
            TCPLayerLite.enqueueDataToSend(data, TCPLayerLite.deviceType.SERVER);
        }

        /// <summary>
        /// Copy the specified resource
        /// </summary>
        /// <param name="newName">The new resource name</param>
        /// <param name="name">The resource to copy</param>
        /// <param name="gameName">The game name</param>
        public static void sendCopyResource(string newName, string name, string gameName)
        {
            XmlDocument XD = new XmlDocument();
            XD.AppendChild(XD.CreateXmlDeclaration("1.0", "UTF-8", null));

            XmlNode root = XD.CreateElement("command");
            XD.AppendChild(root);

            XmlAttribute xa = XD.CreateAttribute("commandType");
            xa.Value = ((int)commands.commandType.RESOURCE_COPY).ToString("D", usCulture);
            root.Attributes.Append(xa);

            xa = XD.CreateAttribute("newName");
            xa.Value = newName;
            root.Attributes.Append(xa);

            xa = XD.CreateAttribute("name");
            xa.Value = name;
            root.Attributes.Append(xa);

            xa = XD.CreateAttribute("gameName");
            xa.Value = gameName;
            root.Attributes.Append(xa);

            byte[] data;
            using (MemoryStream MS = new MemoryStream())
            {
                XmlWriterSettings settings = new XmlWriterSettings();
                settings.Encoding = System.Text.Encoding.UTF8;
                using (XmlWriter XW = XmlWriter.Create(MS, settings))
                {
                    XD.Save(XW);
                    data = MS.ToArray();
                }
            }
            TCPLayerLite.enqueueDataToSend(data, TCPLayerLite.deviceType.SERVER);
        }

        /// <summary>
        /// Rename the specified resource
        /// </summary>
        /// <param name="newName">The new resource name</param>
        /// <param name="name">The resource to rename</param>
        /// <param name="gameName">The game name</param>
        public static void sendRenameResource(string newName, string name, string gameName)
        {
            XmlDocument XD = new XmlDocument();
            XD.AppendChild(XD.CreateXmlDeclaration("1.0", "UTF-8", null));

            XmlNode root = XD.CreateElement("command");
            XD.AppendChild(root);

            XmlAttribute xa = XD.CreateAttribute("commandType");
            xa.Value = ((int)commands.commandType.RESOURCE_RENAME).ToString("D", usCulture);
            root.Attributes.Append(xa);

            xa = XD.CreateAttribute("newName");
            xa.Value = newName;
            root.Attributes.Append(xa);

            xa = XD.CreateAttribute("name");
            xa.Value = name;
            root.Attributes.Append(xa);

            xa = XD.CreateAttribute("gameName");
            xa.Value = gameName;
            root.Attributes.Append(xa);

            byte[] data;
            using (MemoryStream MS = new MemoryStream())
            {
                XmlWriterSettings settings = new XmlWriterSettings();
                settings.Encoding = System.Text.Encoding.UTF8;
                using (XmlWriter XW = XmlWriter.Create(MS, settings))
                {
                    XD.Save(XW);
                    data = MS.ToArray();
                }
            }
            TCPLayerLite.enqueueDataToSend(data, TCPLayerLite.deviceType.SERVER);
        }

        /// <summary>
        /// Change the description of the game
        /// </summary>
        /// <param name="resourceName">The resource name</param>
        /// <param name="gameName">The game name</param>
        /// <param name="description">The new description</param>
        public static void sendChangeDescriptionResource(string resourceName, string gameName, string description)
        {
            XmlDocument XD = new XmlDocument();
            XD.AppendChild(XD.CreateXmlDeclaration("1.0", "UTF-8", null));

            XmlNode root = XD.CreateElement("command");
            XD.AppendChild(root);

            XmlAttribute xa = XD.CreateAttribute("commandType");
            xa.Value = ((int)commands.commandType.RESOURCE_SET_DESCRIPTION).ToString("D", usCulture);
            root.Attributes.Append(xa);

            xa = XD.CreateAttribute("name");
            xa.Value = resourceName;
            root.Attributes.Append(xa);

            xa = XD.CreateAttribute("gameName");
            xa.Value = gameName;
            root.Attributes.Append(xa);

            xa = XD.CreateAttribute("description");
            xa.Value = description;
            root.Attributes.Append(xa);

            byte[] data;
            using (MemoryStream MS = new MemoryStream())
            {
                XmlWriterSettings settings = new XmlWriterSettings();
                settings.Encoding = System.Text.Encoding.UTF8;
                using (XmlWriter XW = XmlWriter.Create(MS, settings))
                {
                    XD.Save(XW);
                    data = MS.ToArray();
                }
            }
            TCPLayerLite.enqueueDataToSend(data, TCPLayerLite.deviceType.SERVER);
        }

        /// <summary>
        /// Get the game archive
        /// </summary>
        /// <param name="gameName">The game name</param>
        /// <param name="author">The author of the archive</param>
        /// <param name="description">The description of the archive</param>
        public static void sendGetGameArchive(string gameName, string author, string description)
        {
            XmlDocument XD = new XmlDocument();
            XD.AppendChild(XD.CreateXmlDeclaration("1.0", "UTF-8", null));

            XmlNode root = XD.CreateElement("command");
            XD.AppendChild(root);

            XmlAttribute xa = XD.CreateAttribute("commandType");
            xa.Value = ((int)commands.commandType.GAME_ARCHIVE_GET).ToString("D", usCulture);
            root.Attributes.Append(xa);

            xa = XD.CreateAttribute("gameName");
            xa.Value = gameName;
            root.Attributes.Append(xa);

            xa = XD.CreateAttribute("author");
            xa.Value = author;
            root.Attributes.Append(xa);

            xa = XD.CreateAttribute("description");
            xa.Value = description;
            root.Attributes.Append(xa);

            byte[] data;
            using (MemoryStream MS = new MemoryStream())
            {
                XmlWriterSettings settings = new XmlWriterSettings();
                settings.Encoding = System.Text.Encoding.UTF8;
                using (XmlWriter XW = XmlWriter.Create(MS, settings))
                {
                    XD.Save(XW);
                    data = MS.ToArray();
                }
            }
            TCPLayerLite.enqueueDataToSend(data, TCPLayerLite.deviceType.SERVER);
        }

        /// <summary>
        /// Get the project archive
        /// </summary>
        /// <param name="name">The project name</param>
        /// <param name="gameName">The game name</param>
        /// <param name="author">The author of the archive</param>
        /// <param name="description">The description of the archive</param>
        public static void sendGetProjectArchive(string name, string gameName, string author, string description)
        {
            XmlDocument XD = new XmlDocument();
            XD.AppendChild(XD.CreateXmlDeclaration("1.0", "UTF-8", null));

            XmlNode root = XD.CreateElement("command");
            XD.AppendChild(root);

            XmlAttribute xa = XD.CreateAttribute("commandType");
            xa.Value = ((int)commands.commandType.PROJECT_ARCHIVE_GET).ToString("D", usCulture);
            root.Attributes.Append(xa);

            xa = XD.CreateAttribute("name");
            xa.Value = name;
            root.Attributes.Append(xa);

            xa = XD.CreateAttribute("gameName");
            xa.Value = gameName;
            root.Attributes.Append(xa);

            xa = XD.CreateAttribute("author");
            xa.Value = author;
            root.Attributes.Append(xa);

            xa = XD.CreateAttribute("description");
            xa.Value = description;
            root.Attributes.Append(xa);

            byte[] data;
            using (MemoryStream MS = new MemoryStream())
            {
                XmlWriterSettings settings = new XmlWriterSettings();
                settings.Encoding = System.Text.Encoding.UTF8;
                using (XmlWriter XW = XmlWriter.Create(MS, settings))
                {
                    XD.Save(XW);
                    data = MS.ToArray();
                }
            }
            TCPLayerLite.enqueueDataToSend(data, TCPLayerLite.deviceType.SERVER);
        }

        /// <summary>
        /// Get the template archive
        /// </summary>
        /// <param name="name">The template control name</param>
        /// <param name="gameName">The game name</param>
        /// <param name="author">The author of the archive</param>
        /// <param name="description">The description of the archive</param>
        public static void sendGetCtrlTemplateArchive(string name, string gameName, string author, string description)
        {
            XmlDocument XD = new XmlDocument();
            XD.AppendChild(XD.CreateXmlDeclaration("1.0", "UTF-8", null));

            XmlNode root = XD.CreateElement("command");
            XD.AppendChild(root);

            XmlAttribute xa = XD.CreateAttribute("commandType");
            xa.Value = ((int)commands.commandType.CTRL_TEMPLATE_ARCHIVE_GET).ToString("D", usCulture);
            root.Attributes.Append(xa);

            xa = XD.CreateAttribute("name");
            xa.Value = name;
            root.Attributes.Append(xa);

            xa = XD.CreateAttribute("gameName");
            xa.Value = gameName;
            root.Attributes.Append(xa);

            xa = XD.CreateAttribute("author");
            xa.Value = author;
            root.Attributes.Append(xa);

            xa = XD.CreateAttribute("description");
            xa.Value = description;
            root.Attributes.Append(xa);

            byte[] data;
            using (MemoryStream MS = new MemoryStream())
            {
                XmlWriterSettings settings = new XmlWriterSettings();
                settings.Encoding = System.Text.Encoding.UTF8;
                using (XmlWriter XW = XmlWriter.Create(MS, settings))
                {
                    XD.Save(XW);
                    data = MS.ToArray();
                }
            }
            TCPLayerLite.enqueueDataToSend(data, TCPLayerLite.deviceType.SERVER);
        }

        /// <summary>
        /// Get resource archive
        /// </summary>
        /// <param name="name">The resource name</param>
        /// <param name="gameName">The game name</param>
        /// <param name="author">The author of the archive</param>
        /// <param name="description">The description of the archive</param>
        public static void sendGetResourceArchive(string name, string gameName, string author, string description)
        {
            XmlDocument XD = new XmlDocument();
            XD.AppendChild(XD.CreateXmlDeclaration("1.0", "UTF-8", null));

            XmlNode root = XD.CreateElement("command");
            XD.AppendChild(root);

            XmlAttribute xa = XD.CreateAttribute("commandType");
            xa.Value = ((int)commands.commandType.RESOURCE_ARCHIVE_GET).ToString("D", usCulture);
            root.Attributes.Append(xa);

            xa = XD.CreateAttribute("name");
            xa.Value = name;
            root.Attributes.Append(xa);

            xa = XD.CreateAttribute("gameName");
            xa.Value = gameName;
            root.Attributes.Append(xa);

            xa = XD.CreateAttribute("author");
            xa.Value = author;
            root.Attributes.Append(xa);

            xa = XD.CreateAttribute("description");
            xa.Value = description;
            root.Attributes.Append(xa);

            byte[] data;
            using (MemoryStream MS = new MemoryStream())
            {
                XmlWriterSettings settings = new XmlWriterSettings();
                settings.Encoding = System.Text.Encoding.UTF8;
                using (XmlWriter XW = XmlWriter.Create(MS, settings))
                {
                    XD.Save(XW);
                    data = MS.ToArray();
                }
            }
            TCPLayerLite.enqueueDataToSend(data, TCPLayerLite.deviceType.SERVER);
        }

        /// <summary>
        /// Set game with archive
        /// </summary>
        /// <param name="gameName">The game name</param>
        /// <param name="data">The PPK archive</param>
        public static void sendSetGameArchive(string gameName, byte[] data)
        {
            XmlDocument XD = new XmlDocument();
            XD.AppendChild(XD.CreateXmlDeclaration("1.0", "UTF-8", null));

            XmlNode root = XD.CreateElement("command");
            XD.AppendChild(root);

            XmlAttribute xa = XD.CreateAttribute("commandType");
            xa.Value = ((int)commands.commandType.GAME_ARCHIVE_SET).ToString("D", usCulture);
            root.Attributes.Append(xa);

            xa = XD.CreateAttribute("gameName");
            xa.Value = gameName;
            root.Attributes.Append(xa);

            xa = XD.CreateAttribute("data");
            xa.Value = Convert.ToBase64String(data);
            root.Attributes.Append(xa);

            byte[] dataToSend;
            using (MemoryStream MS = new MemoryStream())
            {
                XmlWriterSettings settings = new XmlWriterSettings();
                settings.Encoding = System.Text.Encoding.UTF8;
                using (XmlWriter XW = XmlWriter.Create(MS, settings))
                {
                    XD.Save(XW);
                    dataToSend = MS.ToArray();
                }
            }
            TCPLayerLite.enqueueDataToSend(dataToSend, TCPLayerLite.deviceType.SERVER);
        }

        /// <summary>
        /// Set project with archive
        /// </summary>
        /// <param name="name">The project name</param>
        /// <param name="gameName">The game name</param>
        /// <param name="data">The PPK archive</param>
        public static void sendSetProjectArchive(string name, string gameName, byte[] data)
        {
            XmlDocument XD = new XmlDocument();
            XD.AppendChild(XD.CreateXmlDeclaration("1.0", "UTF-8", null));

            XmlNode root = XD.CreateElement("command");
            XD.AppendChild(root);

            XmlAttribute xa = XD.CreateAttribute("commandType");
            xa.Value = ((int)commands.commandType.PROJECT_ARCHIVE_SET).ToString("D", usCulture);
            root.Attributes.Append(xa);

            xa = XD.CreateAttribute("name");
            xa.Value = name;
            root.Attributes.Append(xa);

            xa = XD.CreateAttribute("gameName");
            xa.Value = gameName;
            root.Attributes.Append(xa);

            xa = XD.CreateAttribute("data");
            xa.Value = Convert.ToBase64String(data);
            root.Attributes.Append(xa);

            byte[] dataToSend;
            using (MemoryStream MS = new MemoryStream())
            {
                XmlWriterSettings settings = new XmlWriterSettings();
                settings.Encoding = System.Text.Encoding.UTF8;
                using (XmlWriter XW = XmlWriter.Create(MS, settings))
                {
                    XD.Save(XW);
                    dataToSend = MS.ToArray();
                }
            }
            TCPLayerLite.enqueueDataToSend(dataToSend, TCPLayerLite.deviceType.SERVER);
        }

        /// <summary>
        /// Set template control
        /// </summary>
        /// <param name="name">The template control name</param>
        /// <param name="gameName">The game name</param>
        /// <param name="data">The PPK archive</param>
        public static void sendSetCtrlTemplateArchive(string name, string gameName, byte[] data)
        {
            XmlDocument XD = new XmlDocument();
            XD.AppendChild(XD.CreateXmlDeclaration("1.0", "UTF-8", null));

            XmlNode root = XD.CreateElement("command");
            XD.AppendChild(root);

            XmlAttribute xa = XD.CreateAttribute("commandType");
            xa.Value = ((int)commands.commandType.CTRL_TEMPLATE_ARCHIVE_SET).ToString("D", usCulture);
            root.Attributes.Append(xa);

            xa = XD.CreateAttribute("name");
            xa.Value = name;
            root.Attributes.Append(xa);

            xa = XD.CreateAttribute("gameName");
            xa.Value = gameName;
            root.Attributes.Append(xa);

            xa = XD.CreateAttribute("data");
            xa.Value = Convert.ToBase64String(data);
            root.Attributes.Append(xa);

            byte[] dataToSend;
            using (MemoryStream MS = new MemoryStream())
            {
                XmlWriterSettings settings = new XmlWriterSettings();
                settings.Encoding = System.Text.Encoding.UTF8;
                using (XmlWriter XW = XmlWriter.Create(MS, settings))
                {
                    XD.Save(XW);
                    dataToSend = MS.ToArray();
                }
            }
            TCPLayerLite.enqueueDataToSend(dataToSend, TCPLayerLite.deviceType.SERVER);
        }

        /// <summary>
        /// Set resource archive
        /// </summary>
        /// <param name="name">The resource name</param>
        /// <param name="gameName">The game name</param>
        /// <param name="data">The PPK archive</param>
        public static void sendSetResourceArchive(string name, string gameName, byte[] data)
        {
            XmlDocument XD = new XmlDocument();
            XD.AppendChild(XD.CreateXmlDeclaration("1.0", "UTF-8", null));

            XmlNode root = XD.CreateElement("command");
            XD.AppendChild(root);

            XmlAttribute xa = XD.CreateAttribute("commandType");
            xa.Value = ((int)commands.commandType.RESOURCE_ARCHIVE_SET).ToString("D", usCulture);
            root.Attributes.Append(xa);

            xa = XD.CreateAttribute("name");
            xa.Value = name;
            root.Attributes.Append(xa);

            xa = XD.CreateAttribute("gameName");
            xa.Value = gameName;
            root.Attributes.Append(xa);

            xa = XD.CreateAttribute("data");
            xa.Value = Convert.ToBase64String(data);
            root.Attributes.Append(xa);

            byte[] dataToSend;
            using (MemoryStream MS = new MemoryStream())
            {
                XmlWriterSettings settings = new XmlWriterSettings();
                settings.Encoding = System.Text.Encoding.UTF8;
                using (XmlWriter XW = XmlWriter.Create(MS, settings))
                {
                    XD.Save(XW);
                    dataToSend = MS.ToArray();
                }
            }
            TCPLayerLite.enqueueDataToSend(dataToSend, TCPLayerLite.deviceType.SERVER);
        }

        /// <summary>
        /// Send configuration mode to server
        /// </summary>
        /// <param name="SM">Server mode</param>
        public static void sendConfigureServer(serverMode SM)
        {
            XmlDocument XD = new XmlDocument();
            XD.AppendChild(XD.CreateXmlDeclaration("1.0", "UTF-8", null));

            XmlNode root = XD.CreateElement("command");
            XD.AppendChild(root);

            XmlAttribute xa = XD.CreateAttribute("commandType");
            xa.Value = ((int)commands.commandType.EXTINPUT_CONFIGURE_SERVER).ToString("D", usCulture);
            root.Attributes.Append(xa);

            xa = XD.CreateAttribute("serverMode");
            xa.Value = ((int)SM).ToString("D", usCulture);
            root.Attributes.Append(xa);

            byte[] data;
            using (MemoryStream MS = new MemoryStream())
            {
                XmlWriterSettings settings = new XmlWriterSettings();
                settings.Encoding = System.Text.Encoding.UTF8;
                using (XmlWriter XW = XmlWriter.Create(MS, settings))
                {
                    XD.Save(XW);
                    data = MS.ToArray();
                }
            }
            TCPLayerLite.enqueueDataToSend(data, TCPLayerLite.deviceType.SERVER);
        }

        /// <summary>
        /// Resynch controls with all remotes
        /// </summary>
        public static void sendResynchData()
        {
            List<EMData> curCommandstatus;
            lock (commandStatus)
            {
                curCommandstatus = new List<EMData>(commandStatus.Count);
                foreach (EMData EMD in commandStatus)
                {
                    curCommandstatus.Add(EMD.Copy());
                }
            }

            XmlDocument XD = new XmlDocument();
            XD.AppendChild(XD.CreateXmlDeclaration("1.0", "UTF-8", null));

            XmlNode root = XD.CreateElement("command");
            XD.AppendChild(root);

            XmlAttribute xa = XD.CreateAttribute("commandType");
            xa.Value = ((int)commands.commandType.EXTINPUT_RESYNCH_DATA).ToString("D", usCulture);
            root.Attributes.Append(xa);

            foreach (EMData EMD in curCommandstatus)
            {
                EMD.appendToXmlNode(ref root);
            }

            byte[] data;
            using (MemoryStream MS = new MemoryStream())
            {
                XmlWriterSettings settings = new XmlWriterSettings();
                settings.Encoding = System.Text.Encoding.UTF8;
                using (XmlWriter XW = XmlWriter.Create(MS, settings))
                {
                    XD.Save(XW);
                    data = MS.ToArray();
                }
            }
            TCPLayerLite.enqueueDataToSend(data, TCPLayerLite.deviceType.SERVER);
        }

        /// <summary>
        /// Send pull request to server
        /// </summary>
        public static void sendPullRequest()
        {
            XmlDocument XD = new XmlDocument();
            XD.AppendChild(XD.CreateXmlDeclaration("1.0", "UTF-8", null));

            XmlNode root = XD.CreateElement("command");
            XD.AppendChild(root);

            XmlAttribute xa = XD.CreateAttribute("commandType");
            xa.Value = ((int)commands.commandType.EXTINPUT_PULL_DATA).ToString("D", usCulture);
            root.Attributes.Append(xa);

            byte[] data;
            using (MemoryStream MS = new MemoryStream())
            {
                XmlWriterSettings settings = new XmlWriterSettings();
                settings.Encoding = System.Text.Encoding.UTF8;
                using (XmlWriter XW = XmlWriter.Create(MS, settings))
                {
                    XD.Save(XW);
                    data = MS.ToArray();
                }
            }
            TCPLayerLite.enqueueDataToSend(data, TCPLayerLite.deviceType.SERVER);
        }

        /// <summary>
        /// Send pull request to server with specified extended input tags
        /// </summary>
        /// <param name="EMTags">The list of extended input tags</param>
        public static void sendPullRequest(List<string> EMTags)
        {
            XmlDocument XD = new XmlDocument();
            XD.AppendChild(XD.CreateXmlDeclaration("1.0", "UTF-8", null));

            XmlNode root = XD.CreateElement("command");
            XD.AppendChild(root);

            XmlAttribute xa = XD.CreateAttribute("commandType");
            xa.Value = ((int)commands.commandType.EXTINPUT_PULL_DATA).ToString("D", usCulture);
            root.Attributes.Append(xa);

            StringBuilder SB = new StringBuilder();
            foreach (string EMTag in EMTags)
            {
                SB.Append(EMTag);
                SB.Append(';');
            }
            xa = XD.CreateAttribute("EMTags");
            xa.Value = SB.ToString();
            root.Attributes.Append(xa);

            byte[] data;
            using (MemoryStream MS = new MemoryStream())
            {
                XmlWriterSettings settings = new XmlWriterSettings();
                settings.Encoding = System.Text.Encoding.UTF8;
                using (XmlWriter XW = XmlWriter.Create(MS, settings))
                {
                    XD.Save(XW);
                    data = MS.ToArray();
                }
            }
            TCPLayerLite.enqueueDataToSend(data, TCPLayerLite.deviceType.SERVER);
        }

        /// <summary>
        /// Push data to server
        /// </summary>
        /// <param name="data">The data to push</param>
        public static void sendPushData(List<EMData> data)
        {
            XmlDocument XD = new XmlDocument();
            XD.AppendChild(XD.CreateXmlDeclaration("1.0", "UTF-8", null));

            XmlNode root = XD.CreateElement("command");
            XD.AppendChild(root);

            XmlAttribute xa = XD.CreateAttribute("commandType");
            xa.Value = ((int)commands.commandType.EXTINPUT_PUSH_DATA).ToString("D", usCulture);
            root.Attributes.Append(xa);

            foreach (EMData EMD in data)
            {
                EMD.appendToXmlNode(ref root);
            }

            byte[] dataToSend;
            using (MemoryStream MS = new MemoryStream())
            {
                XmlWriterSettings settings = new XmlWriterSettings();
                settings.Encoding = System.Text.Encoding.UTF8;
                using (XmlWriter XW = XmlWriter.Create(MS, settings))
                {
                    XD.Save(XW);
                    dataToSend = MS.ToArray();
                }
            }
            TCPLayerLite.enqueueDataToSend(dataToSend, TCPLayerLite.deviceType.SERVER);
        }

        /// <summary>
        /// ask the server to load joystick data
        /// </summary>
        /// <param name="projectName">The project name</param>
        /// <param name="gameName">The game name</param>
        public static void sendRealJoyRemapApplyRules(string projectName, string gameName)
        {
            XmlDocument XD = new XmlDocument();
            XD.AppendChild(XD.CreateXmlDeclaration("1.0", "UTF-8", null));

            XmlNode root = XD.CreateElement("command");
            XD.AppendChild(root);

            XmlAttribute xa = XD.CreateAttribute("commandType");
            xa.Value = ((int)commands.commandType.REAL_JOY_REMAP_APPLY_RULES).ToString("D", usCulture);
            root.Attributes.Append(xa);

            xa = XD.CreateAttribute("projectName");
            xa.Value = projectName;
            root.Attributes.Append(xa);

            xa = XD.CreateAttribute("gameName");
            xa.Value = gameName;
            root.Attributes.Append(xa);

            byte[] data;
            using (MemoryStream MS = new MemoryStream())
            {
                XmlWriterSettings settings = new XmlWriterSettings();
                settings.Encoding = System.Text.Encoding.UTF8;
                using (XmlWriter XW = XmlWriter.Create(MS, settings))
                {
                    XD.Save(XW);
                    data = MS.ToArray();
                }
            }
            TCPLayerLite.enqueueDataToSend(data, TCPLayerLite.deviceType.SERVER);
        }

    }
}
