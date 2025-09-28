import { createRouter, createWebHashHistory } from 'vue-router';

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
      component: () => import('@/views/GnssView.vue')
    },
    {
      path: '/camera',
      name: 'camera',
      component: () => import('@/views/CameraView.vue')
    },
    {
      path: '/imu',
      name: 'imu',
      component: () => import('@/views/ImuView.vue')
    },
    {
      path: '/encoder',
      name: 'encoder',
      component: () => import('@/views/EncoderView.vue')
    },
    {
      path: '/wifi',
      name: 'wifi',
      component: () => import('@/views/WiFiView.vue')
    },
    {
      path: '/logging',
      name: 'logging',
      component: () => import('@/views/LoggingView.vue')
    },
    {
      path: '/system',
      name: 'system',
      component: () => import('@/views/SystemView.vue')
    }
  ]
});

export default router;