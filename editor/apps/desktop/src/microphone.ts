/**
 * Whether the OS lets the app record. On macOS every audio input counts as the microphone (an interface's instrument
 * input included), and the engine host is covered by the app's own consent, as its child process. Without consent
 * CoreAudio still opens the device and delivers silence, so the engine looks healthy and hears nothing.
 * `restricted` is a Mac whose owner cannot grant it (a managed profile, parental controls).
 */
export type MicrophoneAccess = 'granted' | 'denied' | 'restricted' | 'not-determined';

/** The part of Electron's systemPreferences this uses. */
export interface MediaAccess {
  getMediaAccessStatus(mediaType: 'microphone'): string;
  askForMediaAccess(mediaType: 'microphone'): Promise<boolean>;
}

/** Where System Settings shows the apps allowed to record (Privacy & Security ▸ Microphone). */
export const MicrophoneSettingsUrl = 'x-apple.systempreferences:com.apple.preference.security?Privacy_Microphone';

/** The current answer, without asking. Only macOS gates audio inputs this way: elsewhere it is always `granted`. */
export function microphoneAccess(platform: NodeJS.Platform, access: MediaAccess): MicrophoneAccess {
  if (platform !== 'darwin') {
    return 'granted';
  }

  const status = access.getMediaAccessStatus('microphone');
  return status === 'granted' || status === 'denied' || status === 'restricted' ? status : 'not-determined';
}

/**
 * The answer, after showing macOS's prompt if the user has never been asked. Call it before the engine opens an
 * input, so the prompt comes from the app the user is looking at and the engine starts with the answer already given.
 */
export async function requestMicrophone(platform: NodeJS.Platform, access: MediaAccess): Promise<MicrophoneAccess> {
  const status = microphoneAccess(platform, access);
  if (status !== 'not-determined') {
    return status;
  }

  return (await access.askForMediaAccess('microphone')) ? 'granted' : 'denied';
}
