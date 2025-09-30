import { createRouter, createWebHashHistory } from 'vue-router';
import GnssView from '@/views/GnssView.vue';
import CameraView from '@/views/CameraView.vue';
import ImuView from '@/views/ImuView.vue';
import EncoderView from '@/views/EncoderView.vue';
import WiFiView from '@/views/WiFiView.vue';
import LoggingView from '@/views/LoggingView.vue';
import SystemView from '@/views/SystemView.vue';

const router = createRouter({
  history: createWebHashHistory(),
  routes: [
    {
      path: '/',
      redirect: '/gnss'
    },
    {
      path: '/gnss',
      name: 'gnss',
      component: GnssView
    },
    {
      path: '/camera',
      name: 'camera',
      component: CameraView
    },
    {
      path: '/imu',
      name: 'imu',
      component: ImuView
    },
    {
      path: '/encoder',
      name: 'encoder',
      component: EncoderView
    },
    {
      path: '/wifi',
      name: 'wifi',
      component: WiFiView
    },
    {
      path: '/logging',
      name: 'logging',
      component: LoggingView
    },
    {
      path: '/system',
      name: 'system',
      component: SystemView
    }
  ]
});

export default router;