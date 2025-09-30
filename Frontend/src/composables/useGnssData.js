import { reactive, ref, computed } from 'vue';

// Global reactive state for GNSS data
const gnssData = reactive({
  // Connection status
  connected: false,

  // Fix status
  fixType: null,
  rtkMode: null,

  // Position (will be updated by real GNSS data)
  latitude: null,
  longitude: null,
  altitude: null,

  // Accuracy estimates
  hAcc: null,
  vAcc: null,

  // Dilution of Precision
  hdop: null,
  vdop: null,
  pdop: null,
  tdop: null,

  // GNSS Time
  gnssTimestamp: null,
  timeValid: null,

  // Satellite counts
  satellitesUsed: null,
  satellitesTracked: null,

  // Constellation breakdown
  constellations: {
    gps: { used: null, tracked: null },
    glonass: { used: null, tracked: null },
    galileo: { used: null, tracked: null },
    beidou: { used: null, tracked: null },
    sbas: { used: null, tracked: null }
  },

  // Per-satellite data
  satellites: [],

  // RTK-specific metrics
  rtk: {
    arRatio: null,
    correctionAge: null,
    baselineLength: null,
    relativeAccuracy: { north: null, east: null, down: null }
  },

  // Survey-In status (for base station mode)
  surveyIn: {
    active: false,
    valid: false,
    duration: null,
    observations: null,
    accuracyMm: null,
    position: { x: null, y: null, z: null }
  },

  // Corrections mode and status
  corrections: {
    mode: 'Disabled', // 'Disabled', 'Receive', 'Send'
    status: {
      source: 'None',
      status: 'Unknown',
      age: null,
      valid: false,
      stale: false,
      sbas: false,
      rtcm: false,
      spartn: false,
      numMessages: 0
    }
  },

  // Reference station position (for base station mode)
  referenceStation: {
    stationId: null,
    latitude: null,
    longitude: null,
    altitude: null
  }
});

const messageRates = reactive({
  messageRates: {},
  timestamp: null
});

// RTK mode management
const isChangingMode = ref(false);
const selectedMode = ref('Disabled');

const modeOptions = [
  {
    value: 'Disabled',
    label: 'Disabled',
    description: 'RTK corrections disabled',
    color: 'slate'
  },
  {
    value: 'Send',
    label: 'Base Station',
    description: 'Send RTK corrections to rovers',
    color: 'blue'
  },
  {
    value: 'Receive',
    label: 'Rover',
    description: 'Receive RTK corrections',
    color: 'purple'
  }
];

const getModeConfig = (mode) => 
{
  return modeOptions.find(option => option.value === mode) || modeOptions[0];
};

const currentModeConfig = computed(() => getModeConfig(gnssData.corrections.mode || 'Disabled'));

// Create a single reactive state object
const state = reactive({
  gnssData,
  messageRates,
  isChangingMode,
  selectedMode,
  currentModeConfig
});

// SignalR event handlers for GNSS data
export function registerGnssEvents(connection) 
{
  connection.on("SatelliteUpdate", (data) => 
  {
    // Update connection status
    gnssData.connected = data.connected ?? false;

    // Update satellite data from NAV-SAT messages
    gnssData.satellitesTracked = data.numSatellites;
    gnssData.satellites = data.satellites.map(sat => ({
      svid: sat.svId,
      constellation: sat.gnssName,
      used: sat.svUsed,
      cn0: sat.cno,
      elevation: sat.elevation,
      azimuth: sat.azimuth,
      health: sat.health,
      qualityIndicator: sat.qualityIndicator,
      pseudorangeResidual: sat.pseudorangeResidual,
      differentialCorrection: sat.differentialCorrection,
      smoothed: sat.smoothed
    }));

    // Update constellation breakdown
    const constellations = {
      gps: { used: 0, tracked: 0 },
      glonass: { used: 0, tracked: 0 },
      galileo: { used: 0, tracked: 0 },
      beidou: { used: 0, tracked: 0 },
      sbas: { used: 0, tracked: 0 }
    };

    data.satellites.forEach(sat =>
    {
      switch (sat.gnssName.toLowerCase())
      {
        case 'gps':
          constellations.gps.tracked++;
          if (sat.svUsed) constellations.gps.used++;
          break;
        case 'glonass':
          constellations.glonass.tracked++;
          if (sat.svUsed) constellations.glonass.used++;
          break;
        case 'galileo':
          constellations.galileo.tracked++;
          if (sat.svUsed) constellations.galileo.used++;
          break;
        case 'beidou':
          constellations.beidou.tracked++;
          if (sat.svUsed) constellations.beidou.used++;
          break;
        case 'sbas':
          constellations.sbas.tracked++;
          if (sat.svUsed) constellations.sbas.used++;
          break;
      }
    });

    gnssData.constellations = constellations;
  });

  connection.on("PvtUpdate", (data) =>
  {
    // Update position and navigation data from NAV-PVT messages
    gnssData.latitude = data.latitude;
    gnssData.longitude = data.longitude;
    gnssData.altitude = data.heightMSL / 1000; // Convert mm to m
    gnssData.hAcc = data.horizontalAccuracy / 1000; // Convert mm to m
    gnssData.vAcc = data.verticalAccuracy / 1000; // Convert mm to m

    // Update GNSS time data
    gnssData.gnssTimestamp = data.gnssTimestamp;
    gnssData.timeValid = data.timeValid;

    // Use the enhanced fix type string from backend instead of hardcoded logic
    gnssData.fixType = data.fixTypeString || 'No Fix';

    // Update RTK mode based on fix type string
    if (data.fixTypeString && data.fixTypeString.includes('RTK'))
    {
      gnssData.rtkMode = data.fixTypeString.includes('Fixed') ? 'Fixed' : 'Float';
    }
    else
    {
      gnssData.rtkMode = null;
    }

    gnssData.satellitesUsed = data.numSatellites;
  });

  connection.on("MessageRatesUpdate", (data) => 
  {
    messageRates.messageRates = data.messageRates;
    messageRates.timestamp = data.timestamp;
  });

  connection.on("SurveyInStatus", (data) => 
  {
    gnssData.surveyIn.active = data.active;
    gnssData.surveyIn.valid = data.valid;
    gnssData.surveyIn.duration = data.duration;
    gnssData.surveyIn.observations = data.observations;
    gnssData.surveyIn.accuracyMm = data.accuracyMm;
    gnssData.surveyIn.position = data.position;
  });

  connection.on("CorrectionsStatusUpdate", (data) => 
  {
    gnssData.corrections.mode = data.mode;
    selectedMode.value = data.mode;
  });

  connection.on("ReferenceStationPosition", (data) => 
  {
    gnssData.referenceStation.stationId = data.stationId;
    gnssData.referenceStation.latitude = data.latitude;
    gnssData.referenceStation.longitude = data.longitude;
    gnssData.referenceStation.altitude = data.altitude;
  });

  connection.on("DopUpdate", (data) =>
  {
    gnssData.hdop = data.horizontalDop;
    gnssData.vdop = data.verticalDop;
    gnssData.pdop = data.positionDop;
    gnssData.tdop = data.timeDop;
  });

  connection.on("CorrectionStatusUpdate", (data) =>
  {
    gnssData.corrections.status.source = data.correctionSource;
    gnssData.corrections.status.status = data.correctionStatus;
    gnssData.corrections.status.age = data.correctionAge;
    gnssData.corrections.status.valid = data.correctionValid;
    gnssData.corrections.status.stale = data.correctionStale;
    gnssData.corrections.status.sbas = data.sbasCorrections;
    gnssData.corrections.status.rtcm = data.rtcmCorrections;
    gnssData.corrections.status.spartn = data.spartnCorrections;
    gnssData.corrections.status.numMessages = data.numMessages;
  });

  connection.on("RelativePositionUpdate", (data) =>
  {
    // Update RTK baseline distance and relative accuracy from NAV-RELPOSNED
    gnssData.rtk.baselineLength = data.relPosValid ? data.relPosLength : null;
    gnssData.rtk.relativeAccuracy.north = data.relPosValid ? data.accN : null;
    gnssData.rtk.relativeAccuracy.east = data.relPosValid ? data.accE : null;
    gnssData.rtk.relativeAccuracy.down = data.relPosValid ? data.accD : null;
    
    // Could also add relative position components if needed
    // gnssData.rtk.relPosN = data.relPosN;
    // gnssData.rtk.relPosE = data.relPosE;
    // gnssData.rtk.relPosD = data.relPosD;
    // gnssData.rtk.relPosHeading = data.relPosHeading;
  });
}

// Mode change handler (moved from RtkPanel)
export function handleModeChange(connection, newMode) 
{
  return new Promise(async (resolve, reject) => 
  {
    if (newMode === gnssData.corrections.mode || isChangingMode.value) 
    {
      resolve(false);
      return;
    }

    isChangingMode.value = true;

    try 
    {
      if (connection && connection.state === 'Connected') 
      {
        const success = await connection.invoke('SetOperatingMode', newMode);

        if (!success) 
        {
          console.error('Failed to change mode - server returned false');
          // Reset selected mode on failure
          selectedMode.value = gnssData.corrections.mode || 'Disabled';
          resolve(false);
        }
        else 
        {
          resolve(true);
        }
        // On success, the mode will be updated via SignalR ModeChanged event
      }
      else 
      {
        console.error('No SignalR connection available or connection not in Connected state');
        selectedMode.value = gnssData.corrections.mode || 'Disabled';
        resolve(false);
      }
    }
    catch (error) 
    {
      console.error('Error changing mode:', error);
      // Reset selected mode on error
      selectedMode.value = gnssData.corrections.mode || 'Disabled';
      reject(error);
    }
    finally 
    {
      isChangingMode.value = false;
    }
  });
}

export function useGnssData() 
{
  return {
    // Single state object
    state,
    // Individual methods and utilities
    modeOptions,
    getModeConfig,
    handleModeChange,
    registerGnssEvents
  };
}