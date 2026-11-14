import { theme } from '@medvya/tokens';
import { createContext, useContext } from 'react';

/** What differs between the two apps: the accent (teal clinic, indigo patient) and the patient app's larger text and targets. */
export type Brand = { solid: string; tint: string; target: number; body: number };

// Clinic: 44 px targets, the touch control height on web too; 15 px body (Medvya Mobile design).
export const clinicBrand: Brand = { solid: theme.color.brand700, tint: theme.color.brand50, target: theme.coarse.spacingControl, body: theme.native.body };
// Patient shell: 17 px minimum text and 48 px targets (docs/00-overview/personas.md).
export const patientBrand: Brand = { solid: theme.color.patient700, tint: theme.color.patient50, target: theme.native.targetPatient, body: theme.native.bodyPatient };

const BrandContext = createContext<Brand>(clinicBrand);
export const BrandProvider = BrandContext.Provider;
export const useBrand = () => useContext(BrandContext);
