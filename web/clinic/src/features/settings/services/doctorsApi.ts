import { csrf, post, send } from '../../schedule/services/scheduleApi';
import { toAddBody, toLicenceBody, type Doctor, type DoctorForm, type DoctorList, type LicenceForm } from '../domain/doctors';

// Settings, Doctors (F-09, MDV-P1-007). Ids in paths, details in bodies. There is no call to read a signature image back:
// it is only for printed prescriptions.
export const fetchDoctors = () => send<DoctorList>('/v1/settings/doctors');
export const addDoctor = (form: DoctorForm) => post<Doctor>('/v1/settings/doctors', toAddBody(form));
export const resubmitLicence = (id: string, form: LicenceForm) =>
  send<Doctor>(`/v1/settings/doctors/${id}/licence`, { method: 'PUT', headers: { ...csrf, 'Content-Type': 'application/json' }, body: JSON.stringify(toLicenceBody(form)) });
/** The body is the image itself; the server checks the file's real type and size. */
export const uploadSignature = (id: string, file: File) =>
  send<void>(`/v1/settings/doctors/${id}/signature`, { method: 'PUT', headers: { ...csrf, 'Content-Type': file.type }, body: file });
export const removeSignature = (id: string) => send<void>(`/v1/settings/doctors/${id}/signature`, { method: 'DELETE', headers: csrf });
