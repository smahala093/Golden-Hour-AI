import type { CapacitorConfig } from '@capacitor/cli';

const config: CapacitorConfig = {
  appId: 'ai.goldenhour.app',
  appName: 'Golden Hour AI',
  webDir: 'dist',
  server: { androidScheme: 'https' },
  android: { allowMixedContent: false },
};

export default config;
