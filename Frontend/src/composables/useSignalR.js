import { ref } from 'vue';
import { HubConnectionBuilder, HubConnectionState } from '@microsoft/signalr';
import { registerGnssEvents } from './useGnssData';
import { registerSystemEvents } from './useSystemData';
import { registerConnectionEvents } from './useConnectionData';
import { useHardwareStatus } from './useHardwareStatus';

// SignalR connection state
let connection = null;
const signalrConnection = ref(null);

// Connection status tracking
const connectionStatus = ref('Disconnected');
const retryAttempt = ref(0);
const nextRetryIn = ref(0);
let retryTimer = null;
let retryCountdown = null;

// Current mode tracking
const currentMode = ref('DISABLED');

// Connection management functions
const updateConnectionStatus = () => 
{
  if (!connection) 
  {
    connectionStatus.value = 'Reconnecting';
    return;
  }

  switch (connection.state) 
  {
    case HubConnectionState.Connected:
      connectionStatus.value = 'Connected';
      retryAttempt.value = 0;
      clearRetryTimer();
      break;
    case HubConnectionState.Connecting:
    case HubConnectionState.Disconnected:
      connectionStatus.value = 'Reconnecting';
      // Only start our custom retry after SignalR has given up
      if (connection.state === HubConnectionState.Disconnected) 
      {
        scheduleRetry();
      }
      break;
  }
};

const clearRetryTimer = () => 
{
  if (retryTimer) 
  {
    clearTimeout(retryTimer);
    retryTimer = null;
  }
  if (retryCountdown) 
  {
    clearInterval(retryCountdown);
    retryCountdown = null;
  }
  nextRetryIn.value = 0;
};

const scheduleRetry = () => 
{
  if (retryTimer) return; // Already scheduled

  retryAttempt.value++;
  const delay = 2000; // Fixed 2 second retry interval
  nextRetryIn.value = Math.ceil(delay / 1000);

  // Countdown timer
  retryCountdown = setInterval(() => 
  {
    nextRetryIn.value--;
    if (nextRetryIn.value <= 0) 
    {
      clearInterval(retryCountdown);
      retryCountdown = null;
    }
  }, 1000);

  // Actual retry
  retryTimer = setTimeout(async () => 
  {
    retryTimer = null;
    if (connection && connection.state === HubConnectionState.Disconnected) 
    {
      try 
      {
        connectionStatus.value = 'Reconnecting';
        await connection.start();
        updateConnectionStatus(); // Update status after successful connection
      }
      catch (err) 
      {
        console.error(`SignalR Retry attempt ${retryAttempt.value} failed:`, err);
        updateConnectionStatus();
      }
    }
  }, delay);
};

// Mode change handler
const handleModeChanged = (newMode) => 
{
  console.log(`App.vue handleModeChanged called with: ${newMode}`);
  console.log(`Updating currentMode from ${currentMode.value} to ${newMode}`);
  currentMode.value = newMode;
};

// Initialize SignalR connection
const initializeConnection = async () => 
{
  // Dynamic URL resolution based on environment
  const hubUrl = import.meta.env.DEV
    ? "http://rover.local/datahub"  // Development mode: use hardcoded localhost
    : `${window.location.protocol}//${window.location.hostname}/datahub`;  // Production: use same host as frontend

  console.log(`Environment: ${import.meta.env.DEV ? 'Development' : 'Production'}`);
  console.log(`SignalR Hub URL: ${hubUrl}`);

  connection = new HubConnectionBuilder()
    .withUrl(hubUrl)
    // Remove automatic reconnect - we'll handle it ourselves with 5s intervals
    .build();

  // Connection state change handlers
  connection.onclose((error) => 
  {
    console.log('SignalR connection closed', error);
    updateConnectionStatus();
  });

  // Register all event handlers from different composables
  registerGnssEvents(connection);
  registerSystemEvents(connection);
  registerConnectionEvents(connection);

  // Setup hardware status listener
  const { setupHardwareStatusListener } = useHardwareStatus();
  setupHardwareStatusListener(connection);

  // Mode change event handler
  connection.on("ModeChanged", (data) =>
  {
    console.log(`SignalR ModeChanged event received:`, data);
    console.log(`Setting current mode from ${currentMode.value} to: ${data.Mode}`);
    currentMode.value = data.Mode;
  });

  // Set the connection ref for child components immediately
  signalrConnection.value = connection;
  console.log("SignalR connection object set for child components");

  try 
  {
    connectionStatus.value = 'Reconnecting';
    await connection.start();
    console.log("SignalR Connected successfully!");

    // Get initial mode
    try
    {
      console.log('Requesting initial mode from server...');
      const initialMode = await connection.invoke('GetCurrentMode');
      console.log(`Initial mode retrieved: ${initialMode}`);
      currentMode.value = initialMode;
      console.log(`Current mode state updated to: ${currentMode.value}`);
    }
    catch (error)
    {
      console.error('Failed to get initial mode:', error);
    }

    // Get hardware status
    const { fetchHardwareStatus } = useHardwareStatus();
    await fetchHardwareStatus(connection);

    updateConnectionStatus();
  }
  catch (err) 
  {
    console.error("SignalR Connection Error: ", err);
    updateConnectionStatus();
  }
};

const cleanup = async () => 
{
  clearRetryTimer();
  if (connection) 
  {
    await connection.stop();
  }
};

export function useSignalR() 
{
  return {
    signalrConnection,
    connectionStatus,
    retryAttempt,
    nextRetryIn,
    currentMode,
    initializeConnection,
    cleanup,
    handleModeChanged
  };
}