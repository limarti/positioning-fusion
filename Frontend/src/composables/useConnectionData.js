import { reactive, ref, watch } from 'vue';

// Global reactive state for connection data
const wifiStatus = reactive({
  currentMode: 'Disconnected',
  connectedNetworkSSID: null,
  signalStrength: null,
  isConnected: false,
  lastUpdated: null
});

const knownNetworks = ref([]);
const fallbackNotification = ref(null);

// Mode Preference
const preferredMode = ref(null);

// AP Configuration
const apConfig = reactive({
  ssid: '',
  password: ''
});
const originalAPPassword = ref('');
const isAPPasswordModified = ref(false);

// Dialog state
const showAddNetworkDialog = ref(false);
const dialogNetworkConfig = reactive({
  ssid: '',
  password: ''
});
const showDialogPassword = ref(false);

// UI State
const isConnecting = ref(false);
const showAPPassword = ref(false);
const showNetworkPasswords = ref({});

// Connection handlers
let statusUpdateHandler = null;
let fallbackNotificationHandler = null;
let knownNetworksUpdateHandler = null;

// Initialize watchers
let isWatchersInitialized = false;

const initializeWatchers = () => 
{
  if (isWatchersInitialized) return;
  isWatchersInitialized = true;

  // Watch for AP password modifications
  watch(() => apConfig.password, (newPassword) => 
  {
    isAPPasswordModified.value = newPassword !== originalAPPassword.value;
  });
};

const setupSignalRHandlers = async (connection) => 
{
  statusUpdateHandler = (data) => 
  {
    console.log('WiFi status update:', data);
    wifiStatus.currentMode = data.currentMode;
    wifiStatus.connectedNetworkSSID = data.connectedNetworkSSID;
    wifiStatus.signalStrength = data.signalStrength;
    wifiStatus.isConnected = data.isConnected;
    wifiStatus.lastUpdated = data.lastUpdated;
  };

  fallbackNotificationHandler = (data) => 
  {
    console.log('WiFi fallback notification:', data);
    fallbackNotification.value = {
      message: data.message,
      reason: data.reason,
      timestamp: data.timestamp
    };

    // Auto-hide notification after 10 seconds
    setTimeout(() => 
    {
      fallbackNotification.value = null;
    }, 10000);
  };

  knownNetworksUpdateHandler = (data) => 
  {
    console.log('Known networks update:', data);
    knownNetworks.value = data.networks || [];
  };

  connection.on("WiFiStatusUpdate", statusUpdateHandler);
  connection.on("WiFiFallbackNotification", fallbackNotificationHandler);
  connection.on("WiFiKnownNetworksUpdate", knownNetworksUpdateHandler);
};

const loadInitialData = async (connection) => 
{
  try 
  {
    console.log('Loading WiFi status...');
    const status = await connection.invoke('GetWiFiStatus');
    wifiStatus.currentMode = status.currentMode;
    wifiStatus.connectedNetworkSSID = status.connectedNetworkSSID;
    wifiStatus.signalStrength = status.signalStrength;
    wifiStatus.isConnected = status.isConnected;
    wifiStatus.lastUpdated = status.lastUpdated;
    console.log('WiFi status loaded:', status);

    console.log('Loading known networks...');
    const networks = await connection.invoke('GetKnownNetworks');
    knownNetworks.value = networks || [];
    console.log('Known networks loaded:', networks);

    console.log('Loading preferred mode...');
    const mode = await connection.invoke('GetWiFiPreferredMode');
    console.log('Preferred mode received:', mode);
    preferredMode.value = mode;

    console.log('Loading AP configuration...');
    const apConfiguration = await connection.invoke('GetAPConfiguration');
    console.log('AP configuration received:', apConfiguration);
    apConfig.ssid = apConfiguration.ssid || '';
    apConfig.password = apConfiguration.password || '';
    originalAPPassword.value = apConfiguration.password || '';

    console.log('All initial WiFi data loaded successfully:', { status, networks, preferredMode: mode, apConfig: apConfiguration });
  }
  catch (error) 
  {
    console.error('Error loading initial WiFi data:', error);
    console.error('Error details:', error.message, error.stack);
    throw error; // Don't hide the error
  }
};

const initializeWiFiData = async (connection) => 
{
  if (connection?.state === 'Connected' && preferredMode.value === null) 
  {
    console.log('Initializing WiFi data...');
    await setupSignalRHandlers(connection);
    await loadInitialData(connection);
  }
};

const addNetwork = async (connection) => 
{
  if (!dialogNetworkConfig.ssid || !dialogNetworkConfig.password) 
  {
    return;
  }

  isConnecting.value = true;

  try 
  {
    const success = await connection.invoke('ConnectToWiFi', dialogNetworkConfig.ssid, dialogNetworkConfig.password);

    if (success) 
    {
      console.log('Network connection initiated successfully');
      closeAddNetworkDialog();
    }
    else 
    {
      console.error('Network connection failed');
    }
  }
  catch (error) 
  {
    console.error('Error connecting to network:', error);
  }
  finally 
  {
    isConnecting.value = false;
  }
};

const closeAddNetworkDialog = () => 
{
  showAddNetworkDialog.value = false;
  dialogNetworkConfig.ssid = '';
  dialogNetworkConfig.password = '';
  showDialogPassword.value = false;
};

const removeKnownNetwork = async (connection, ssid) => 
{
  try 
  {
    const success = await connection.invoke('RemoveKnownNetwork', ssid);

    if (success) 
    {
      console.log('Known network removed:', ssid);
    }
    else 
    {
      console.error('Failed to remove known network:', ssid);
    }
  }
  catch (error) 
  {
    console.error('Error removing known network:', error);
  }
};

const saveAPPassword = async (connection) => 
{
  try 
  {
    const success = await connection.invoke('SetAPConfiguration', apConfig.ssid, apConfig.password);

    if (success) 
    {
      console.log('AP password updated successfully');
      originalAPPassword.value = apConfig.password;
      isAPPasswordModified.value = false;
    }
    else 
    {
      console.error('AP password update failed');
    }
  }
  catch (error) 
  {
    console.error('Error updating AP password:', error);
  }
};

const cancelAPPasswordEdit = () => 
{
  apConfig.password = originalAPPassword.value;
  isAPPasswordModified.value = false;
};

const setPreferredMode = async (connection) => 
{
  try 
  {
    const success = await connection.invoke('SetWiFiPreferredMode', preferredMode.value);

    if (success) 
    {
      console.log('WiFi preferred mode updated successfully to:', preferredMode.value);
    }
    else 
    {
      console.error('WiFi preferred mode update failed');
    }
  }
  catch (error) 
  {
    console.error('Error setting WiFi preferred mode:', error);
  }
};

const toggleNetworkPassword = (ssid) => 
{
  showNetworkPasswords.value[ssid] = !showNetworkPasswords.value[ssid];
};

const formatLastConnected = (timestamp) => 
{
  if (!timestamp) return 'Never';
  const date = new Date(timestamp);
  return date.toLocaleDateString() + ' ' + date.toLocaleTimeString([], { hour: '2-digit', minute:'2-digit' });
};

const cleanup = (connection) => 
{
  if (connection) 
  {
    connection.off("WiFiStatusUpdate", statusUpdateHandler);
    connection.off("WiFiFallbackNotification", fallbackNotificationHandler);
    connection.off("WiFiKnownNetworksUpdate", knownNetworksUpdateHandler);
  }
};

// Create a single reactive state object
const state = reactive({
  wifiStatus,
  knownNetworks,
  fallbackNotification,
  preferredMode,
  apConfig,
  originalAPPassword,
  isAPPasswordModified,
  showAddNetworkDialog,
  dialogNetworkConfig,
  showDialogPassword,
  isConnecting,
  showAPPassword,
  showNetworkPasswords
});

// SignalR event handlers for connection data
export function registerConnectionEvents(connection) 
{
  setupSignalRHandlers(connection);
}

export function useConnectionData() 
{
  // Initialize watchers the first time this composable is used
  initializeWatchers();

  return {
    // Single state object
    state,
    // Individual methods and utilities
    setupSignalRHandlers,
    loadInitialData,
    initializeWiFiData,
    addNetwork,
    closeAddNetworkDialog,
    removeKnownNetwork,
    saveAPPassword,
    cancelAPPasswordEdit,
    setPreferredMode,
    toggleNetworkPassword,
    formatLastConnected,
    cleanup,
    registerConnectionEvents
  };
}