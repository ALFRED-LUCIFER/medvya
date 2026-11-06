import { Linking, Platform } from 'react-native';
import { fromCast } from '../../api/services/source';

export const fetchHome = (_personId: string) => fromCast(c => c.home);
export const call = (number: string) => Linking.openURL(`tel:${number}`);
/** Opens the phone's maps app at the clinic. */
export const openDirections = (address: string) => {
  const q = encodeURIComponent(address);
  return Linking.openURL(Platform.OS === 'ios' ? `maps:?q=${q}` : `geo:0,0?q=${q}`);
};
