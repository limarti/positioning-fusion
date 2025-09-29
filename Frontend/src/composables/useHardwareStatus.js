import { ref } from 'vue';

const hardwareStatus = ref({
  gnssAvailable: false,
  imuAvailable: false,
  cameraAvailable: false,
  encoderAvailable: false
});

export function useHardwareStatus() {
  const fetchHardwareStatus = async (connection) => {
    if (!connection) return;

    try {
      const status = await connection.invoke('GetHardwareStatus');
      hardwareStatus.value = status;
    } catch (error) {
      console.error('Failed to get hardware status:', error);
    }
  };

  return {
    hardwareStatus,
    fetchHardwareStatus
  };
}