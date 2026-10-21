import { Box } from '@mui/material';
import { SystemState } from '@medvya/ui';

// A banner over the page, not a replacement, so typed input survives (F-05d).
export default function OfflineBanner({ onRetry }: { onRetry: () => void }) {
  return (
    <Box sx={{ position: 'fixed', insetInlineEnd: 16, bottom: 16, zIndex: 1300 }}>
      <SystemState state="offline" onAction={onRetry} />
    </Box>
  );
}
