namespace DevelopersHub.RealtimeNetworking.Client
{
    using System;
    using System.Collections;
    using System.Collections.Generic;
    using UnityEngine;
    using UnityEngine.SceneManagement;
    using static DevelopersHub.RealtimeNetworking.Client.Packet;

    public class RealtimeNetworking : MonoBehaviour
    {

        #region Events
        public static event NoCallback OnDisconnectedFromServer;
        public static event ActionCallback OnConnectingToServerResult;
        public static event PacketCallback OnPacketReceived;
        public static event AuthCallback OnAuthentication;
        public static event CreateRoomCallback OnCreateRoom;
        public static event GetRoomsCallback OnGetRooms;
        public static event JoinRoomCallback OnJoinRoom;
        public static event LeaveRoomCallback OnLeaveRoom;
        public static event DeleteRoomCallback OnDeleteRoom;
        public static event RoomUpdateCallback OnRoomUpdated;
        public static event KickFromRoomCallback OnKickFromRoom;
        public static event RoomStatusCallback OnChangeRoomStatus;
        public static event StartRoomCallback OnRoomStartGame;
        public static event ChangeOwnerCallback OnOwnerChanged;
        #endregion

        #region Callbacks
        public delegate void ActionCallback(bool successful);
        public delegate void NoCallback();
        public delegate void PacketCallback(Packet packet);
        public delegate void AuthCallback(AuthenticationResponse response);
        public delegate void CreateRoomCallback(CreateRoomResponse response, Data.Room room);
        public delegate void GetRoomsCallback(GetRoomsResponse response, List<Data.Room> rooms);
        public delegate void JoinRoomCallback(JoinRoomResponse response, Data.Room room);
        public delegate void LeaveRoomCallback(LeaveRoomResponse response);
        public delegate void DeleteRoomCallback(DeleteRoomResponse response);
        public delegate void RoomUpdateCallback(RoomUpdateType response, Data.Room room, Data.Player player, Data.Player targetPlayer);
        public delegate void KickFromRoomCallback(KickFromRoomResponse response);
        public delegate void RoomStatusCallback(RoomStatusResponse response, bool ready);
        public delegate void StartRoomCallback(StartRoomResponse response);
        public delegate void ChangeOwnerCallback(NetworkObject target, long oldOwner, long newOwner);
        #endregion

        private bool _initialized = false;
        private bool _authenticated = false; public static bool isAuthenticated { get { return instance._authenticated; } }
        private bool _connected = false; public static bool isConnected { get { return instance._connected; } }
        private bool _inGame = false; public static bool isGameStarted { get { return instance._inGame; } }
        private long _accountID = -1; public static long accountID { get { return instance._accountID; } }
        private long _sceneHostID = -1; public static bool isSceneHost { get { return instance._sceneHostID >= 0 && instance._sceneHostID == instance._accountID; } }

        private string _usernameKey = "username";
        private string _passwordKey = "password";

        private Scene _scene = default; public static int sceneIndex { get { return instance._scene.buildIndex; } }
        private List<NetworkObject> _sceneObjects = new List<NetworkObject>();
        private List<ObjectsData> _globalObjects = new List<ObjectsData>();
        private List<NetworkObject> _instantiatedObjects = new List<NetworkObject>();
        private HashSet<long> _disconnected = new HashSet<long>();
        private int _maxDestroydTrack = 10;
        private List<string> _destroyed = new List<string>();
        private Data.Room _gameRoom = null;
        private int _ticksPerSecond = 10;
        private int _ticksCalled = 0;
        private float _ticksTimer = 0;

        private class ObjectsData
        {
            public int sceneIndex = 0;
            public List<SceneObjectsData> accounts = new List<SceneObjectsData>();
        }

        private class SceneObjectsData
        {
            public long accountID = -1;
            public List<NetworkObject.Data> objects = new List<NetworkObject.Data>();
        }

        private string password
        {
            get
            {
                return PlayerPrefs.HasKey(_passwordKey) ? PlayerPrefs.GetString(_passwordKey) : "";
            }
            set
            {
                if (string.IsNullOrEmpty(value))
                {
                    PlayerPrefs.DeleteKey(_passwordKey);
                }
                else
                {
                    PlayerPrefs.SetString(_passwordKey, value);
                }
            }
        }

        private string username
        {
            get
            {
                return PlayerPrefs.HasKey(_usernameKey) ? PlayerPrefs.GetString(_usernameKey) : "";
            }
            set
            {
                if (string.IsNullOrEmpty(value))
                {
                    PlayerPrefs.DeleteKey(_usernameKey);
                }
                else
                {
                    PlayerPrefs.SetString(_usernameKey, value);
                }
            }
        }

        private static RealtimeNetworking _instance = null; public static RealtimeNetworking instance
        {
            get
            {
                if (_instance == null)
                {
                    _instance = FindObjectOfType<RealtimeNetworking>();
                    if (_instance == null)
                    {
                        _instance = Client.instance.gameObject.AddComponent<RealtimeNetworking>();
                    }
                    _instance.Initialize();
                }
                return _instance;
            }
        }

        private void Awake()
        {
            _scene = SceneManager.GetActiveScene();
            _sceneObjects.Clear();
            NetworkObject[] obj = FindObjectsOfType<NetworkObject>(true);
            if(obj != null)
            {
                _sceneObjects.AddRange(obj);
            }
            SetupScene();
        }

        private void Initialize()
        {
            if (_initialized)
            {
                return;
            }
            _initialized = true;
        }

        private void OnEnable()
        {
            SceneManager.sceneLoaded += OnSceneLoaded;
            SceneManager.sceneUnloaded += OnSceneUnloaded;
        }

        private void OnDisable()
        {
            SceneManager.sceneLoaded -= OnSceneLoaded;
            SceneManager.sceneUnloaded -= OnSceneUnloaded;
        }

        private void Update()
        {
            if(_connected && _authenticated && _inGame)
            {
                _ticksTimer += Time.deltaTime;
                int ticks = Mathf.FloorToInt(_ticksTimer * _ticksPerSecond);
                if(ticks > _ticksCalled)
                {
                    _ticksCalled++;
                    Tick();
                }
                if (_ticksCalled >= _ticksPerSecond)
                {
                    _ticksCalled = 0;
                    _ticksTimer = 0;
                }
            }
        }

        private void Tick()
        {
            if (_sceneObjects != null && _sceneObjects.Count > 0)
            {
                bool useTcp = false;
                List<NetworkObject.Data> syncObjects = new List<NetworkObject.Data>();
                List<NetworkObject.Data> unownedSyncObjects = new List<NetworkObject.Data>();
                List<NetworkObject.ShortData> scriptsData = new List<NetworkObject.ShortData>();
                for (int i = _sceneObjects.Count - 1; i >= 0; i--)
                {
                    if(_sceneObjects[i] != null && _sceneObjects[i])
                    {
                        if (_sceneObjects[i].isOwner && !_sceneObjects[i].isDestroying)
                        {
                            NetworkObject.Data data = _sceneObjects[i].GetData();
                            if (data != null)
                            {
                                if (_sceneObjects[i].ownerID <= 0 && _sceneHostID >= 0 && _sceneHostID == accountID)
                                {
                                    unownedSyncObjects.Add(data);
                                }
                                else
                                {
                                    syncObjects.Add(data);
                                }
                            }
                        }
                        else if (!_sceneObjects[i].isOwner && !_sceneObjects[i].isDestroying && isSceneHost)
                        {
                            NetworkObject.ScriptData[] scripts = _sceneObjects[i].GetVariables(SyncVariable.WhoCanChange.Host);
                            if(scripts != null && scripts.Length > 0)
                            {
                                NetworkObject.ShortData shortData = new NetworkObject.ShortData();
                                shortData.id = _sceneObjects[i].id;
                                shortData.scripts = scripts;
                                scriptsData.Add(shortData);
                            }
                        }
                    }
                    else
                    {
                        _sceneObjects.RemoveAt(i);
                    }
                }
                if(_instantiatedObjects.Count > 0)
                {
                    int o = unownedSyncObjects.Count;
                    for (int i = _instantiatedObjects.Count - 1; i >= 0; i--)
                    {
                        bool found = false;
                        for (int j = 0; j < o; j++)
                        {
                            if (unownedSyncObjects[j].id == _instantiatedObjects[i].id)
                            {
                                found = true;
                                break;
                            }
                        }
                        if (!found)
                        {
                            NetworkObject.Data data = _instantiatedObjects[i].GetData();
                            if (data != null)
                            {
                                useTcp = true;
                                unownedSyncObjects.Add(data);
                            }
                        }
                        _instantiatedObjects.RemoveAt(i);
                    }
                }

                if (syncObjects.Count > 0 || unownedSyncObjects.Count > 0 || scriptsData.Count > 0)
                {
                    byte[] data1 = null, data2 = null, data3 = null;
                    if (syncObjects.Count > 0)
                    {
                        data1 = Tools.Compress(Tools.Serialize<List<NetworkObject.Data>>(syncObjects));
                    }
                    if (scriptsData.Count > 0)
                    {
                        data2 = Tools.Compress(Tools.Serialize<List<NetworkObject.ShortData>>(scriptsData));
                    }
                    if (unownedSyncObjects.Count > 0)
                    {
                        data3 = Tools.Compress(Tools.Serialize<List<NetworkObject.Data>>(unownedSyncObjects));
                    }
                    Packet packet = new Packet();
                    packet.Write((int)InternalID.SYNC_ROOM_PLAYER);
                    packet.Write(sceneIndex);
                    packet.Write(data1 == null ? 0 : data1.Length);
                    packet.Write(data2 == null ? 0 : data2.Length);
                    packet.Write(data3 == null ? 0 : data3.Length);
                    if (data1 != null)
                    {
                        packet.Write(data1);
                    }
                    if (data2 != null)
                    {
                        packet.Write(data2);
                    }
                    if (data3 != null)
                    {
                        packet.Write(data3);
                    }
                    if (useTcp)
                    {
                        SendTCPDataInternal(packet);
                    }
                    else
                    {
                        SendUDPDataInternal(packet);
                    }
                }

                int s = -1;
                for (int i = 0; i < _globalObjects.Count; i++)
                {
                    if (_globalObjects[i].sceneIndex == sceneIndex)
                    {
                        s = i;
                        break;
                    }
                }
                if (s < 0)
                {
                    ObjectsData newData = new ObjectsData();
                    newData.accounts = new List<SceneObjectsData>();
                    newData.sceneIndex = sceneIndex;
                    s = _globalObjects.Count;
                    _globalObjects.Add(newData);
                }
                if (syncObjects.Count > 0)
                {
                    int a = -1;
                    for (int i = 0; i < _globalObjects[s].accounts.Count; i++)
                    {
                        if (_globalObjects[s].accounts[i].accountID == accountID)
                        {
                            a = i;
                            break;
                        }
                    }
                    if (a < 0)
                    {
                        SceneObjectsData newData = new SceneObjectsData();
                        newData.objects = new List<NetworkObject.Data>();
                        newData.accountID = accountID;
                        a = _globalObjects[s].accounts.Count;
                        _globalObjects[s].accounts.Add(newData);
                    }
                    _globalObjects[s].accounts[a].objects = syncObjects;
                }
                if (unownedSyncObjects.Count > 0 && isSceneHost)
                {
                    int u = -1;
                    for (int i = 0; i < _globalObjects[s].accounts.Count; i++)
                    {
                        if (_globalObjects[s].accounts[i].accountID <= 0)
                        {
                            u = i;
                            break;
                        }
                    }
                    if (u < 0)
                    {
                        SceneObjectsData newData = new SceneObjectsData();
                        newData.objects = new List<NetworkObject.Data>();
                        newData.accountID = -1;
                        u = _globalObjects[s].accounts.Count;
                        _globalObjects[s].accounts.Add(newData);
                    }
                    _globalObjects[s].accounts[u].objects = unownedSyncObjects;
                }
            }
        }

        private void TickReceived(int scene, long account, long host, byte[] data, byte[] scriptsData, byte[] unownedData)
        {
            bool disconnected = _disconnected.Contains(account);
            if (scene == sceneIndex)
            {
                _sceneHostID = host;
            }
            if (disconnected)
            {
                return;
            }

            List<NetworkObject.Data> syncObjects = new List<NetworkObject.Data>();
            if(data != null)
            {
                syncObjects = Tools.Desrialize<List<NetworkObject.Data>>(Tools.Decompress(data));
            }

            List<NetworkObject.ShortData> scripts = new List<NetworkObject.ShortData>();
            if (scriptsData != null)
            {
                scripts = Tools.Desrialize<List<NetworkObject.ShortData>>(Tools.Decompress(scriptsData));
            }

            List<NetworkObject.Data> unownedSyncObjects = new List<NetworkObject.Data>();
            if (unownedData != null)
            {
                unownedSyncObjects = Tools.Desrialize<List<NetworkObject.Data>>(Tools.Decompress(unownedData));
            }

            int s = -1;
            for (int i = 0; i < _globalObjects.Count; i++)
            {
                if (_globalObjects[i].sceneIndex == scene)
                {
                    s = i;
                    break;
                }
            }
            if(s < 0)
            {
                ObjectsData newData = new ObjectsData();
                newData.accounts = new List<SceneObjectsData>();
                newData.sceneIndex = scene;
                s = _globalObjects.Count;
                _globalObjects.Add(newData);
            }
            int a = -1;
            int u = -1;
            for (int i = 0; i < _globalObjects[s].accounts.Count; i++)
            {
                if (_globalObjects[s].accounts[i].accountID == account)
                {
                    a = i;
                    break;
                }
            }
            if (a < 0)
            {
                SceneObjectsData newData = new SceneObjectsData();
                newData.objects = new List<NetworkObject.Data>();
                newData.accountID = account;
                a = _globalObjects[s].accounts.Count;
                _globalObjects[s].accounts.Add(newData);
            }
            if (unownedSyncObjects.Count > 0)
            {
                for (int i = 0; i < _globalObjects[s].accounts.Count; i++)
                {
                    if (_globalObjects[s].accounts[i].accountID <= 0)
                    {
                        u = i;
                        break;
                    }
                }
                if (u < 0)
                {
                    SceneObjectsData newData = new SceneObjectsData();
                    newData.objects = new List<NetworkObject.Data>();
                    newData.accountID = -1;
                    u = _globalObjects[s].accounts.Count;
                    _globalObjects[s].accounts.Add(newData);
                }
            }
            if (scene == sceneIndex)
            {
                int o = 0;
                for (int i = 0; i < _sceneObjects.Count; i++)
                {
                    if (_sceneObjects[i] != null)
                    {
                        bool found = false;
                        for (int j = o; j < syncObjects.Count; j++)
                        {
                            if (syncObjects[j].id == _sceneObjects[i].id)
                            {
                                NetworkObject.Data taragetData = syncObjects[j];

                                for (int k = j; k > o; k--)
                                {
                                    syncObjects[k] = syncObjects[k - 1];
                                }
                                syncObjects[o] = taragetData;

                                o++;
                                _sceneObjects[i]._ApplyData(taragetData);
                                found = true;
                                break;
                            }
                        }
                        if (!found && _sceneObjects[i].ownerID >= 0)
                        {
                            _sceneObjects[i]._SetWithoutData();
                        }
                    }
                }
                if (o < syncObjects.Count)
                {
                    for (int i = o; i < syncObjects.Count; i++)
                    {
                        // Instantiate the data without object
                        if (syncObjects[i].prefab >= 0 && syncObjects[i].prefab < Client.instance.settings.prefabs.Length && Client.instance.settings.prefabs[syncObjects[i].prefab] != null && !_destroyed.Contains(syncObjects[i].id))
                        {
                            NetworkObject networkObject = Instantiate(Client.instance.settings.prefabs[syncObjects[i].prefab], syncObjects[i].transform.position, syncObjects[i].transform.rotation);
                            networkObject.id = syncObjects[i].id;
                            networkObject.prefabIndex = syncObjects[i].prefab;
                            networkObject._Initialize(account, syncObjects[i].destroy);
                            networkObject._ApplyData(syncObjects[i]);
                            // Keep track of networkObject in a list and instantiate it after leaving and coming back to scene
                            _sceneObjects.Add(networkObject);
                        }
                    }
                }

                // Unowned
                if (unownedSyncObjects.Count > 0)
                {
                    o = 0;
                    for (int i = 0; i < _sceneObjects.Count; i++)
                    {
                        if (_sceneObjects[i] != null)
                        {
                            bool found = false;
                            for (int j = o; j < unownedSyncObjects.Count; j++)
                            {
                                if (unownedSyncObjects[j].id == _sceneObjects[i].id)
                                {
                                    NetworkObject.Data taragetData = unownedSyncObjects[j];

                                    for (int k = j; k > o; k--)
                                    {
                                        unownedSyncObjects[k] = unownedSyncObjects[k - 1];
                                    }
                                    unownedSyncObjects[o] = taragetData;

                                    o++;
                                    _sceneObjects[i]._ApplyData(taragetData);
                                    found = true;
                                    break;
                                }
                            }
                            if (!found && _sceneObjects[i].ownerID < 0)
                            {
                                _sceneObjects[i]._SetWithoutData();
                            }
                        }
                    }
                    if (o < unownedSyncObjects.Count)
                    {
                        for (int i = o; i < unownedSyncObjects.Count; i++)
                        {
                            // Instantiate the data without object
                            if (unownedSyncObjects[i].prefab >= 0 && unownedSyncObjects[i].prefab < Client.instance.settings.prefabs.Length && Client.instance.settings.prefabs[unownedSyncObjects[i].prefab] != null && !_destroyed.Contains(unownedSyncObjects[i].id))
                            {
                                NetworkObject networkObject = Instantiate(Client.instance.settings.prefabs[unownedSyncObjects[i].prefab], unownedSyncObjects[i].transform.position, unownedSyncObjects[i].transform.rotation);
                                networkObject.id = unownedSyncObjects[i].id;
                                networkObject.prefabIndex = unownedSyncObjects[i].prefab;
                                networkObject._Initialize(-1, unownedSyncObjects[i].destroy);
                                networkObject._ApplyData(unownedSyncObjects[i]);
                                // Keep track of networkObject in a list and instantiate it after leaving and coming back to scene
                                _sceneObjects.Add(networkObject);
                            }
                        }
                    }
                }

                for (int i = _sceneObjects.Count - 1; i >= 0; i--)
                {
                    if (_sceneObjects[i] != null) { continue; }
                    _sceneObjects.RemoveAt(i);
                }

                if(scripts.Count > 0 && _sceneObjects.Count > 0)
                {
                    o = 0;
                    for (int i = 0; i < _sceneObjects.Count; i++)
                    {
                        if (_sceneObjects[i] != null)
                        {
                            for (int j = o; j < scripts.Count; j++)
                            {
                                if (scripts[j].id == _sceneObjects[i].id)
                                {
                                    NetworkObject.ShortData taragetData = scripts[j];

                                    for (int k = j; k > o; k--)
                                    {
                                        scripts[k] = scripts[k - 1];
                                    }
                                    scripts[o] = taragetData;

                                    o++;
                                    _sceneObjects[i]._ApplyData(taragetData);
                                    break;
                                }
                            }
                        }
                    }
                }
            }
            _globalObjects[s].accounts[a].objects = syncObjects;
            if (unownedSyncObjects.Count > 0)
            {
                _globalObjects[s].accounts[u].objects = unownedSyncObjects;
            }
        }

        private void OnSceneLoaded(Scene scene, LoadSceneMode mode)
        {
            _sceneHostID = -1;
            _scene = scene;
            _sceneObjects.Clear();
            NetworkObject[] obj = FindObjectsOfType<NetworkObject>(true);
            if (obj != null)
            {
                _sceneObjects.AddRange(obj);
            }
            SetupScene();
        }

        private void OnSceneUnloaded(Scene scene)
        {

        }

        private void SetupScene()
        {
            if(_sceneObjects.Count > 0 && _globalObjects.Count > 0)
            {
                for (int i = 0; i < _globalObjects.Count; i++)
                {
                    if (_globalObjects[i].sceneIndex == sceneIndex)
                    {
                        for (int j = 0; j < _sceneObjects.Count; j++)
                        {
                            if (_sceneObjects[j] != null)
                            {
                                bool found = false;
                                for (int k = 0; k < _globalObjects[i].accounts.Count; k++)
                                {
                                    for (int o = 0; o < _globalObjects[i].accounts[k].objects.Count; o++)
                                    {
                                        if (_globalObjects[i].accounts[k].objects[o].id == _sceneObjects[j].id)
                                        {
                                            _sceneObjects[j]._Initialize(_globalObjects[i].accounts[k].accountID, _globalObjects[i].accounts[k].objects[o].destroy);
                                            _sceneObjects[j]._ApplyData(_globalObjects[i].accounts[k].objects[o]);
                                            found = true;
                                            break;
                                        }
                                    }
                                    if (found)
                                    {
                                        break;
                                    }
                                }
                                if (!found)
                                {
                                    // Destroy ?
                                }
                            }
                        }
                        break;
                    }
                }
            }
        }

        public static void Connect()
        {
            Client.instance.ConnectToServer();
        }

        public void _Connection(bool result)
        {
            _connected = result;
            if (OnConnectingToServerResult != null)
            {
                OnConnectingToServerResult.Invoke(result);
            }
        }

        public void _Disconnected()
        {
            _globalObjects.Clear();
            _inGame = false;
            _gameRoom = null;
            _connected = false;
            _accountID = -1;
            _authenticated = false;
            if (OnDisconnectedFromServer != null)
            {
                OnDisconnectedFromServer.Invoke();
            }
        }

        public void _ReceivePacket(Packet packet)
        {
            if (OnPacketReceived != null)
            {
                OnPacketReceived.Invoke(packet);
            }
        }

        private static void SendTCPDataInternal(Packet _packet)
        {
            if (_packet == null)
            {
                return;
            }
            _packet.SetID((int)Packet.ID.INTERNAL);
            _packet.WriteLength();
            Client.instance.tcp.SendData(_packet);
        }

        private static void SendUDPDataInternal(Packet _packet)
        {
            if (_packet == null)
            {
                return;
            }
            _packet.SetID((int)Packet.ID.INTERNAL);
            _packet.WriteLength();
            Client.instance.udp.SendData(_packet);
        }

        public void _ReceiveInternal(Packet packet)
        {
            int id = packet.ReadInt();
            switch ((InternalID)id)
            {
                case InternalID.AUTH:
                    int authRes = packet.ReadInt();
                    long authID = packet.ReadLong();
                    int authBanned = packet.ReadInt();
                    string authUser = packet.ReadString();
                    string authPass = packet.ReadString();
                    packet.Dispose();
                    if(authRes == (int)AuthenticationResponse.SUCCESSFULL)
                    {
                        _accountID = authID;
                        _authenticated = true;
                        username = authUser;
                        password = authPass;
                    }
                    if (OnAuthentication != null)
                    {
                        OnAuthentication.Invoke((AuthenticationResponse)authRes);
                    }
                    break;
                case InternalID.CREATE_ROOM:
                    int crRoomRes = packet.ReadInt();
                    Data.Room crRoom = null;
                    if (crRoomRes == (int)CreateRoomResponse.SUCCESSFULL)
                    {
                        int crRoomBytesLen = packet.ReadInt();
                        byte[] crRoomBytes = packet.ReadBytes(crRoomBytesLen);
                        crRoom = Tools.Desrialize<Data.Room>(Tools.Decompress(crRoomBytes));
                    }
                    packet.Dispose();
                    if (OnCreateRoom != null)
                    {
                        OnCreateRoom.Invoke((CreateRoomResponse)crRoomRes, crRoom);
                    }
                    break;
                case InternalID.GET_ROOMS:
                    if (OnGetRooms != null)
                    {
                        int gtRoomsBytesLen = packet.ReadInt();
                        byte[] gtRoomsBytes = packet.ReadBytes(gtRoomsBytesLen);
                        List<Data.Room> gtRooms = Tools.Desrialize<List<Data.Room>>(Tools.Decompress(gtRoomsBytes));
                        OnGetRooms.Invoke(GetRoomsResponse.SUCCESSFULL, gtRooms);
                    }
                    packet.Dispose();
                    break;
                case InternalID.JOIN_ROOM:
                    if (OnJoinRoom != null)
                    {
                        int jnRoomRes = packet.ReadInt();
                        Data.Room jnRoom = null;
                        if (jnRoomRes == (int)JoinRoomResponse.SUCCESSFULL)
                        {
                            int jnRoomBytesLen = packet.ReadInt();
                            byte[] jnRoomBytes = packet.ReadBytes(jnRoomBytesLen);
                            jnRoom = Tools.Desrialize<Data.Room>(Tools.Decompress(jnRoomBytes));
                        }
                        OnJoinRoom.Invoke((JoinRoomResponse)jnRoomRes, jnRoom);
                    }
                    packet.Dispose();
                    break;
                case InternalID.LEAVE_ROOM:
                    if (OnLeaveRoom != null)
                    {
                        int lvRoomRes = packet.ReadInt();
                        OnLeaveRoom.Invoke((LeaveRoomResponse)lvRoomRes);
                    }
                    packet.Dispose();
                    break;
                case InternalID.DELETE_ROOM:
                    if (OnDeleteRoom != null)
                    {
                        int deRoomRes = packet.ReadInt();
                        OnDeleteRoom.Invoke((DeleteRoomResponse)deRoomRes);
                    }
                    packet.Dispose();
                    break;
                case InternalID.ROOM_UPDATED:
                    int upRoomTyp = packet.ReadInt();
                    Data.Room upRoom = null;
                    Data.Player upPlayer = null;
                    Data.Player upTargetPlayer = null;

                    int upBytesLen = packet.ReadInt();
                    byte[] upBytes = packet.ReadBytes(upBytesLen);
                    upRoom = Tools.Desrialize<Data.Room>(Tools.Decompress(upBytes));
                    upBytesLen = packet.ReadInt();
                    upBytes = packet.ReadBytes(upBytesLen);
                    upPlayer = Tools.Desrialize<Data.Player>(Tools.Decompress(upBytes));
                    if (upRoomTyp == (int)RoomUpdateType.PLAYER_KICKED)
                    {
                        upBytesLen = packet.ReadInt();
                        upBytes = packet.ReadBytes(upBytesLen);
                        upTargetPlayer = Tools.Desrialize<Data.Player>(Tools.Decompress(upBytes));
                    }

                    packet.Dispose();

                    if (upRoomTyp == (int)RoomUpdateType.PLAYER_KICKED)
                    {
                        if (upTargetPlayer.id == accountID)
                        {
                            _inGame = false;
                        }
                        PlayerLeftGame(upTargetPlayer.id);
                    }
                    else if (upRoomTyp == (int)RoomUpdateType.PLAYER_LEFT)
                    {
                        if (upPlayer.id == accountID)
                        {
                            _inGame = false;
                        }
                        PlayerLeftGame(upPlayer.id);
                    }
                    else if (upRoomTyp == (int)RoomUpdateType.ROOM_DELETED)
                    {
                        _inGame = false;
                    }
                    else if (upRoomTyp == (int)RoomUpdateType.GAME_STARTED)
                    {
                        _globalObjects.Clear();
                        _inGame = true;
                        _ticksTimer = 0;
                        _ticksCalled = 0;
                        _disconnected.Clear();
                    }

                    if (_gameRoom != null && _gameRoom.hostID == upRoom.hostID)
                    {
                        // OnHostChanged
                    }
                    _gameRoom = upRoom;

                    if (OnRoomUpdated != null)
                    {
                        OnRoomUpdated.Invoke((RoomUpdateType)upRoomTyp, upRoom, upPlayer, upTargetPlayer);
                    }
                    break;
                case InternalID.KICK_FROM_ROOM:
                    int kcRoomRes = packet.ReadInt();
                    packet.Dispose();
                    if (OnKickFromRoom != null)
                    {
                        OnKickFromRoom.Invoke((KickFromRoomResponse)kcRoomRes);
                    }
                    break;
                case InternalID.STATUS_IN_ROOM:
                    int csRoomRes = packet.ReadInt();
                    bool csRoomRdy = packet.ReadBool();
                    packet.Dispose();
                    if (OnChangeRoomStatus != null)
                    {
                        OnChangeRoomStatus.Invoke((RoomStatusResponse)csRoomRes, csRoomRdy);
                    }
                    break;
                case InternalID.START_ROOM:
                    int stRoomRes = packet.ReadInt();
                    packet.Dispose();
                    if (OnRoomStartGame != null)
                    {
                        OnRoomStartGame.Invoke((StartRoomResponse)stRoomRes);
                    }
                    break;
                case InternalID.SYNC_ROOM_PLAYER:
                    int syScene = packet.ReadInt();
                    long syAccount = packet.ReadLong();
                    long syHost = packet.ReadLong();
                    int syDataLen1 = packet.ReadInt();
                    int syDataLen2 = packet.ReadInt();
                    int syDataLen3 = packet.ReadInt();
                    byte[] syData1 = null;
                    byte[] syData2 = null;
                    byte[] syData3 = null;
                    if (syDataLen1 > 0)
                    {
                        syData1 = packet.ReadBytes(syDataLen1);
                    }
                    if (syDataLen2 > 0)
                    {
                        syData2 = packet.ReadBytes(syDataLen2);
                    }
                    if (syDataLen3 > 0)
                    {
                        syData3 = packet.ReadBytes(syDataLen3);
                    }
                    packet.Dispose();
                    TickReceived(syScene, syAccount, syHost, syData1, syData2, syData3);
                    break;
                case InternalID.SET_HOST:
                    int stScene = packet.ReadInt();
                    long stHost = packet.ReadLong();
                    packet.Dispose();
                    if(sceneIndex == stScene)
                    {
                        _sceneHostID = stHost;
                    }
                    break;
                case InternalID.DESTROY_OBJECT:
                    int dsScene = packet.ReadInt();
                    long dsAccount = packet.ReadLong();
                    string dsId = packet.ReadString();
                    System.Numerics.Vector3 dsPos = packet.ReadVector3();
                    packet.Dispose();
                    _DestroyObject(dsScene, dsId, dsAccount, new Vector3(dsPos.X, dsPos.Y, dsPos.Z));
                    break;
                case InternalID.CHANGE_OWNER:
                    int coScene = packet.ReadInt();
                    long coAccount = packet.ReadLong();
                    int coDataLen = packet.ReadInt();
                    byte[] coData = packet.ReadBytes(coDataLen);
                    long coOwner = packet.ReadLong();
                    packet.Dispose();
                    _ChangeOwner(coScene, coData, coAccount, coOwner);
                    break;
                case InternalID.CHANGE_OWNER_CONFIRM:
                    int cofScene = packet.ReadInt();
                    string cofId = packet.ReadString();
                    System.Numerics.Vector3 cofPos = packet.ReadVector3(); // Todo
                    long cofOwner = packet.ReadLong();
                    packet.Dispose();
                    if(sceneIndex == cofScene)
                    {
                        for (int i = 0; i < _sceneObjects.Count; i++)
                        {
                            if (_sceneObjects[i].id == cofId)
                            {
                                long cfold = _sceneObjects[i].ownerID;
                                _sceneObjects[i]._Initialize(cofOwner, _sceneObjects[i].destroyOnLeave);
                                if (OnOwnerChanged != null)
                                {
                                    OnOwnerChanged.Invoke(_sceneObjects[i], cfold, cofOwner);
                                }
                                break;
                            }
                        }
                    }
                    break;
            }
        }

        private void PlayerLeftGame(long id)
        {
            _disconnected.Add(id);
            for (int i = 0; i < _globalObjects.Count; i++)
            {
                for (int j = _globalObjects[i].accounts.Count - 1; j >= 0; j--)
                {
                    if (_globalObjects[i].accounts[j].accountID == id)
                    {
                        for (int k = _globalObjects[i].accounts[j].objects.Count - 1; k >= 0; k--)
                        {
                            if (_globalObjects[i].accounts[j].objects[k].destroy)
                            {
                                _globalObjects[i].accounts[j].objects.RemoveAt(k);
                            }
                        }
                        if (_globalObjects[i].accounts.Count <= 0)
                        {
                            _globalObjects[i].accounts.RemoveAt(j);
                        }
                        break;
                    }
                }
            }
            for (int i = _sceneObjects.Count - 1; i >= 0; i--)
            {
                if (_sceneObjects[i].ownerID == id && _sceneObjects[i].destroyOnLeave)
                {
                    Destroy(_sceneObjects[i].gameObject);
                    _sceneObjects.RemoveAt(i);
                }
            }
        }

        public static void Authenticate()
        {
            _Authenticate(instance.username, instance.password);
        }

        public static void Authenticate(string username, string password, bool createIfNotExist)
        {
            if (string.IsNullOrEmpty(username))
            {
                if (OnAuthentication != null)
                {
                    OnAuthentication.Invoke(AuthenticationResponse.INVALID_INPUT);
                }
            }
            else if (string.IsNullOrEmpty(password))
            {
                if (OnAuthentication != null)
                {
                    OnAuthentication.Invoke(AuthenticationResponse.INVALID_INPUT);
                }
            }
            else
            {
                _Authenticate(username, Tools.EncrypteToMD5(password), createIfNotExist);
            }
        }

        private static void _Authenticate(string username, string password, bool createIfNotExist = true)
        {
            if (!instance._connected)
            {
                if (OnAuthentication != null)
                {
                    OnAuthentication.Invoke(AuthenticationResponse.NOT_CONNECTED);
                }
            }
            else if (instance._authenticated)
            {
                if (OnAuthentication != null)
                {
                    OnAuthentication.Invoke(AuthenticationResponse.ALREADY_AUTHENTICATED);
                }
            }
            else
            {
                Packet packet = new Packet();
                packet.Write((int)InternalID.AUTH);
                packet.Write(SystemInfo.deviceUniqueIdentifier);
                packet.Write(createIfNotExist);
                packet.Write(username);
                packet.Write(password);
                SendTCPDataInternal(packet);
            }
        }

        public static void CreateRoom(int gameID, int team)
        {
            CreateRoom(gameID, team, "");
        }

        public static void CreateRoom(int gameID, int team, string password)
        {
            if (!instance._connected)
            {
                if (OnCreateRoom != null)
                {
                    OnCreateRoom.Invoke(CreateRoomResponse.NOT_CONNECTED, null);
                }
            }
            else if (!instance._authenticated)
            {
                if (OnCreateRoom != null)
                {
                    OnCreateRoom.Invoke(CreateRoomResponse.NOT_AUTHENTICATED, null);
                }
            }
            else
            {
                if (!string.IsNullOrEmpty(password))
                {
                    password = Tools.EncrypteToMD5(password);
                }
                Packet packet = new Packet();
                packet.Write((int)InternalID.CREATE_ROOM);
                packet.Write(password);
                packet.Write(gameID);
                packet.Write(team);
                SendTCPDataInternal(packet);
            }
        }

        public static void GetRooms()
        {
            if (!instance._connected)
            {
                if (OnGetRooms != null)
                {
                    OnGetRooms.Invoke(GetRoomsResponse.NOT_CONNECTED, null);
                }
            }
            else if (!instance._authenticated)
            {
                if (OnGetRooms != null)
                {
                    OnGetRooms.Invoke(GetRoomsResponse.NOT_AUTHENTICATED, null);
                }
            }
            else
            {
                Packet packet = new Packet();
                packet.Write((int)InternalID.GET_ROOMS);
                SendTCPDataInternal(packet);
            }
        }

        public static void JoinRoom(string roomID, int team)
        {
            JoinRoom(roomID, team, "");
        }

        public static void JoinRoom(string roomID, int team, string password)
        {
            if (!instance._connected)
            {
                if (OnJoinRoom != null)
                {
                    OnJoinRoom.Invoke(JoinRoomResponse.NOT_CONNECTED, null);
                }
            }
            else if (!instance._authenticated)
            {
                if (OnJoinRoom != null)
                {
                    OnJoinRoom.Invoke(JoinRoomResponse.NOT_AUTHENTICATED, null);
                }
            }
            else
            {
                if (!string.IsNullOrEmpty(password))
                {
                    password = Tools.EncrypteToMD5(password);
                }
                Packet packet = new Packet();
                packet.Write((int)InternalID.JOIN_ROOM);
                packet.Write(roomID);
                packet.Write(password);
                packet.Write(team);
                SendTCPDataInternal(packet);
            }
        }

        public static void LeaveRoom()
        {
            if (!instance._connected)
            {
                if (OnLeaveRoom != null)
                {
                    OnLeaveRoom.Invoke(LeaveRoomResponse.NOT_CONNECTED);
                }
            }
            else if (!instance._authenticated)
            {
                if (OnLeaveRoom != null)
                {
                    OnLeaveRoom.Invoke(LeaveRoomResponse.NOT_AUTHENTICATED);
                }
            }
            else
            {
                Packet packet = new Packet();
                packet.Write((int)InternalID.LEAVE_ROOM);
                SendTCPDataInternal(packet);
            }
        }

        public static void DeleteRoom()
        {
            if (!instance._connected)
            {
                if (OnDeleteRoom != null)
                {
                    OnDeleteRoom.Invoke(DeleteRoomResponse.NOT_CONNECTED);
                }
            }
            else if (!instance._authenticated)
            {
                if (OnDeleteRoom != null)
                {
                    OnDeleteRoom.Invoke(DeleteRoomResponse.NOT_AUTHENTICATED);
                }
            }
            else
            {
                Packet packet = new Packet();
                packet.Write((int)InternalID.DELETE_ROOM);
                SendTCPDataInternal(packet);
            }
        }

        public static void KickFromRoom(long targetID)
        {
            if (!instance._connected)
            {
                if (OnKickFromRoom != null)
                {
                    OnKickFromRoom.Invoke(KickFromRoomResponse.NOT_CONNECTED);
                }
            }
            else if (!instance._authenticated)
            {
                if (OnKickFromRoom != null)
                {
                    OnKickFromRoom.Invoke(KickFromRoomResponse.NOT_AUTHENTICATED);
                }
            }
            else
            {
                Packet packet = new Packet();
                packet.Write((int)InternalID.KICK_FROM_ROOM);
                packet.Write(targetID);
                SendTCPDataInternal(packet);
            }
        }

        public static void ChangeRoomStatus(bool ready)
        {
            if (!instance._connected)
            {
                if (OnChangeRoomStatus != null)
                {
                    OnChangeRoomStatus.Invoke(RoomStatusResponse.NOT_CONNECTED, false);
                }
            }
            else if (!instance._authenticated)
            {
                if (OnChangeRoomStatus != null)
                {
                    OnChangeRoomStatus.Invoke(RoomStatusResponse.NOT_AUTHENTICATED, false);
                }
            }
            else
            {
                Packet packet = new Packet();
                packet.Write((int)InternalID.STATUS_IN_ROOM);
                packet.Write(ready);
                SendTCPDataInternal(packet);
            }
        }

        public static void StartRoomGame()
        {
            if (!instance._connected)
            {
                if (OnRoomStartGame != null)
                {
                    OnRoomStartGame.Invoke(StartRoomResponse.NOT_CONNECTED);
                }
            }
            else if (!instance._authenticated)
            {
                if (OnRoomStartGame != null)
                {
                    OnRoomStartGame.Invoke(StartRoomResponse.NOT_AUTHENTICATED);
                }
            }
            else
            {
                Packet packet = new Packet();
                packet.Write((int)InternalID.START_ROOM);
                SendTCPDataInternal(packet);
            }
        }

        public static NetworkObject InstantiatePrefab(int index, Vector3 position, Quaternion rotation, bool own = true, bool destroyOnLeave = false)
        {
            return instance._Instantiate(index, position, rotation, own, destroyOnLeave);
        }

        private NetworkObject _Instantiate(int prefabIndex, Vector3 position, Quaternion rotation, bool own, bool destroyOnLeave)
        {
            NetworkObject _object = null;
            if (prefabIndex >= 0 && prefabIndex < Client.instance.settings.prefabs.Length && Client.instance.settings.prefabs[prefabIndex] != null)
            {
                _object = Instantiate(Client.instance.settings.prefabs[prefabIndex], position, rotation);
                _object.id = Guid.NewGuid().ToString();
                _object.prefabIndex = prefabIndex;
                _object._Initialize(own ? accountID : -1, destroyOnLeave);
                _object.Initialize();
                StartCoroutine(_Instantiate(_object, own));
            }
            return _object;
        }

        private IEnumerator _Instantiate(NetworkObject _object, bool own)
        {
            yield return new WaitForEndOfFrame();
            _sceneObjects.Add(_object);
            if (!own && !isSceneHost)
            {
                _instantiatedObjects.Add(_object);
            }
            // Keep track of _object in a list and instantiate it after leaving and coming back to scene
            _ticksCalled--;
        }

        private enum InternalID
        {
            AUTH = 1, GET_ROOMS = 2, CREATE_ROOM = 3, JOIN_ROOM = 4, LEAVE_ROOM = 5, DELETE_ROOM = 6, ROOM_UPDATED = 7, KICK_FROM_ROOM = 8, STATUS_IN_ROOM = 9, START_ROOM = 10, SYNC_ROOM_PLAYER = 11, SET_HOST = 12, DESTROY_OBJECT = 13, CHANGE_OWNER = 14, CHANGE_OWNER_CONFIRM = 15
        }

        public enum AuthenticationResponse
        {
            UNKNOWN = 0, SUCCESSFULL = 1, NOT_CONNECTED = 2, NOT_AUTHENTICATED = 3, ALREADY_AUTHENTICATED = 4, USERNAME_TAKEN = 5, WRONG_CREDS = 6, BANNED = 7, INVALID_INPUT = 8
        }

        public enum CreateRoomResponse
        {
            UNKNOWN = 0, SUCCESSFULL = 1, NOT_CONNECTED = 2, NOT_AUTHENTICATED = 3, ALREADY_IN_ANOTHER_ROOM = 4
        }

        public enum GetRoomsResponse
        {
            UNKNOWN = 0, SUCCESSFULL = 1, NOT_CONNECTED = 2, NOT_AUTHENTICATED = 3
        }

        public enum JoinRoomResponse
        {
            UNKNOWN = 0, SUCCESSFULL = 1, NOT_CONNECTED = 2, NOT_AUTHENTICATED = 3, ALREADY_IN_ANOTHER_ROOM = 4, WRONG_PASSWORD = 5, AT_FULL_CAPACITY = 6, ALREADY_GAME_STARTED = 7
        }

        public enum LeaveRoomResponse
        {
            UNKNOWN = 0, SUCCESSFULL = 1, NOT_CONNECTED = 2, NOT_AUTHENTICATED = 3, NOT_IN_ANY_ROOM = 4
        }

        public enum DeleteRoomResponse
        {
            UNKNOWN = 0, SUCCESSFULL = 1, NOT_CONNECTED = 2, NOT_AUTHENTICATED = 3, NOT_IN_ANY_ROOM = 4, DONT_HAVE_PERMISSION = 5
        }

        public enum RoomUpdateType
        {
            UNKNOWN = 0, ROOM_DELETED = 1, PLAYER_JOINED = 2, PLAYER_LEFT = 3, PLAYER_STATUS_CHANGED = 4, PLAYER_KICKED = 5, GAME_STARTED = 6
        }

        public enum KickFromRoomResponse
        {
            UNKNOWN = 0, SUCCESSFULL = 1, NOT_CONNECTED = 2, NOT_AUTHENTICATED = 3, NOT_IN_ANY_ROOM = 4, DONT_HAVE_PERMISSION = 5, TARGET_NOT_FOUND = 6
        }

        public enum RoomStatusResponse
        {
            UNKNOWN = 0, SUCCESSFULL = 1, NOT_CONNECTED = 2, NOT_AUTHENTICATED = 3, NOT_IN_ANY_ROOM = 4, ALREADY_IN_THAT_STATUS = 5
        }

        public enum StartRoomResponse
        {
            UNKNOWN = 0, SUCCESSFULL = 1, NOT_CONNECTED = 2, NOT_AUTHENTICATED = 3, NOT_IN_ANY_ROOM = 4, DONT_HAVE_PERMISSION = 5, ALREADY_STARTED = 6
        }

        public enum ChangeOwnerResponse
        {
            UNKNOWN = 0, SUCCESSFULL = 1, NOT_CONNECTED = 2, NOT_AUTHENTICATED = 3, NOT_IN_Game = 4, DONT_HAVE_PERMISSION = 5
        }

        private void _DestroyObject(int scene, string id, long account, Vector3 position)
        {
            _destroyed.Add(id);
            if(_destroyed.Count > _maxDestroydTrack)
            {
                _destroyed.RemoveAt(0);
            }
            if (sceneIndex == scene)
            {
                for (int i = _sceneObjects.Count - 1; i >= 0; i--)
                {
                    if (_sceneObjects[i] != null)
                    {
                        if (_sceneObjects[i].id == id)
                        {
                            _sceneObjects[i]._Destroy(position);
                            break;
                        }
                    }
                    else
                    {
                        _sceneObjects.RemoveAt(i);
                    }
                }
            }
            for (int i = 0; i < _globalObjects.Count; i++)
            {
                if (_globalObjects[i].sceneIndex == scene)
                {
                    bool found = false;
                    int u = -1;
                    int a = -1;
                    for (int j = 0; j < _globalObjects[i].accounts.Count; j++)
                    {
                        if (found)
                        {
                            break;
                        }
                        if (_globalObjects[i].accounts[j].accountID == account)
                        {
                            a = j;
                            for (int k = 0; k < _globalObjects[i].accounts[j].objects.Count; k++)
                            {
                                if (_globalObjects[i].accounts[j].objects[k].id == id)
                                {
                                    _globalObjects[i].accounts[j].objects.RemoveAt(k);
                                    found = true;
                                    break;
                                }
                            }
                            if(u >= 0)
                            {
                                break;
                            }
                        }
                        else if (_globalObjects[i].accounts[j].accountID < 0)
                        {
                            u = j;
                            if (a >= 0)
                            {
                                break;
                            }
                        }
                    }
                    if(!found && u >= 0)
                    {
                        for (int k = 0; k < _globalObjects[i].accounts[u].objects.Count; k++)
                        {
                            if (_globalObjects[i].accounts[u].objects[k].id == id)
                            {
                                _globalObjects[i].accounts[u].objects.RemoveAt(k);
                                break;
                            }
                        }
                    }
                    break;
                }
            }
        }

        public void ChangeOwner(NetworkObject target)
        {
            ChangeOwner(target, -1);
        }

        public void ChangeOwner(List<NetworkObject> target)
        {
            ChangeOwner(target, -1);
        }

        public void ChangeOwner(NetworkObject target, long newOwner)
        {
            if(target == null)
            {
                return;
            }
            List<NetworkObject> objects = new List<NetworkObject>();
            objects.Add(target);
            ChangeOwner(objects, newOwner);
        }

        public void ChangeOwner(List<NetworkObject> target, long newOwner)
        {
            if (target == null)
            {

            }
            else if (!instance._connected)
            {

            }
            else if (!instance._authenticated)
            {

            }
            else
            {

                for (int i = target.Count - 1; i >= 0; i--)
                {
                    if (target[i] == null || target[i].ownerID == newOwner || !((target[i].ownerID >= 0 && target[i].isOwner) || target[i].ownerID < 0))
                    {
                        target.RemoveAt(i);
                    }
                }
                if(target.Count > 0)
                {
                    byte[] data = Tools.Compress(Tools.Serialize<List<NetworkObject>>(target));
                    Packet packet = new Packet();
                    packet.Write((int)InternalID.CHANGE_OWNER);
                    packet.Write(sceneIndex);
                    packet.Write(data.Length);
                    packet.Write(data);
                    packet.Write(newOwner);
                    SendTCPDataInternal(packet);
                }
            }
        }

        private void _ChangeOwner(int scene, byte[] coData, long account, long newOwner)
        {
            List<NetworkObject> objects = Tools.Desrialize<List<NetworkObject>>(Tools.Decompress(coData));
            List<NetworkObject> targets = new List<NetworkObject>();
            for (int o = 0; o < objects.Count; o++)
            {
                for (int i = 0; i < _sceneObjects.Count; i++)
                {
                    if (_sceneObjects[i] != null)
                    {
                        if (_sceneObjects[i].id == objects[o].id)
                        {
                            if (_sceneObjects[i].ownerID < 0 || (newOwner < 0 && _sceneObjects[i].ownerID == account))
                            {
                                _sceneObjects[i]._Initialize(newOwner, _sceneObjects[i].destroyOnLeave);
                                Packet packet = new Packet();
                                packet.Write((int)InternalID.CHANGE_OWNER_CONFIRM);
                                packet.Write(sceneIndex);
                                packet.Write(_sceneObjects[i].id);
                                packet.Write(_sceneObjects[i].transform.position);
                                packet.Write(newOwner);
                                packet.Write(account);
                                SendTCPDataInternal(packet);
                            }
                            break;
                        }
                    }
                }
            }
        }

        public void _DestroyObject(NetworkObject target)
        {
            if (target == null)
            {

            }
            else if (!target.canDestroy)
            {

            }
            else if (!instance._connected)
            {

            }
            else if (!instance._authenticated)
            {

            }
            else
            {
                for (int i = _sceneObjects.Count - 1; i >= 0; i--)
                {
                    if (_sceneObjects[i] != null && _sceneObjects[i].id == target.id)
                    {
                        _sceneObjects.RemoveAt(i);
                        break;
                    }
                }
                Packet packet = new Packet();
                packet.Write((int)InternalID.DESTROY_OBJECT);
                packet.Write(sceneIndex);
                packet.Write(target.id);
                packet.Write(target.transform.position);
                _destroyed.Add(target.id);
                if (_destroyed.Count > _maxDestroydTrack)
                {
                    _destroyed.RemoveAt(0);
                }
                SendTCPDataInternal(packet);
                if (!target.isDestroying)
                {
                    target._SetDestroy();
                    Destroy(target.gameObject);
                }
            }
        }

        public void LoadScene(string sceneName, List<NetworkObject> objectsToTransport)
        {
            if (SceneUtility.GetBuildIndexByScenePath(sceneName) >= 0)
            {
                SceneManager.LoadScene(sceneName);
            }
        }

    }
}