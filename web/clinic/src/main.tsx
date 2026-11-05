import { StrictMode } from 'react';
import { createRoot } from 'react-dom/client';
import Root from './app/Root';
import { registerAssetWorker } from './features/connectivity/services/serviceWorker';

createRoot(document.getElementById('root')!).render(<StrictMode><Root /></StrictMode>);
void registerAssetWorker();
