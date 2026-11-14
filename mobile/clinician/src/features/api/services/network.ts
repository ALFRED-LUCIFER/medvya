import * as Network from 'expo-network';

const isOnline = (s: Network.NetworkState) => s.isConnected !== false && s.isInternetReachable !== false;

/** Tells TanStack Query when the phone goes offline or comes back (onlineManager.setEventListener). Returns the unsubscribe. */
export function watchNetwork(setOnline: (online: boolean) => void) {
  void Network.getNetworkStateAsync().then(s => setOnline(isOnline(s)));
  const sub = Network.addNetworkStateListener(s => setOnline(isOnline(s)));
  return () => sub.remove();
}
