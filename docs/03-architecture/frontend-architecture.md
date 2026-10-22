# Front-end architecture: domain-driven features, controller hooks (hard rule)

Applies to `web/clinic` and `web/portal`. Decided in [ADR-009](../adr/ADR-009-frontend-architecture.md). Enforced by lint (`web/clinic/.oxlintrc.json`): a violation fails `pnpm lint` and CI.

## Layers

```
src/
├── main.tsx                  # mounts <Root />
├── app/                      # composition only: providers, routing, wiring controllers → components
├── features/<feature>/       # one folder per domain capability (registration, booking, front-desk, encounter …)
│   ├── domain/               # pure TypeScript: types, value objects, rules (e.g. "PIN must be 6 digits")
│   ├── services/             # I/O only: API calls (generated client), storage, browser APIs
│   ├── hooks/                # controller hooks: useXxxController()
│   └── components/           # presentational React: props in, events out
└── dev/                      # developer-only pages (UI gallery)
```

Shared, feature-independent code lives in packages: `@medvya/ui` (design-system primitives), `@medvya/i18n` (translations and formatting), and later `@medvya/api-client` (generated from the API's OpenAPI document).

## What each layer may do

| Layer | Allowed | Forbidden (lint-enforced) |
|---|---|---|
| `domain/` | Types, pure functions, validation rules, Zod schemas | React, UI kit, services, hooks, components, `fetch`, `window`, `document`, storage |
| `services/` | HTTP via the API client, storage, browser APIs. Returns plain data | React, UI kit, hooks, components |
| `hooks/` (controllers) | React state and effects, **TanStack Query** (`useQuery`/`useMutation`), **React Hook Form**, calling services, applying domain rules. Returns a **view model + handlers** | UI (MUI, components), direct `fetch`/`localStorage` |
| `components/` | Rendering, layout, `useI18n()`, local UI-only state (open/closed, hover) | Services, controller hooks, TanStack, `fetch`, storage |
| `app/` | Calls controller hooks and passes their results to components; providers; router | Business rules (put them in `domain/`) |

## The controller hook pattern

One controller per screen or flow. It is the only place that knows where data comes from. The component never knows.

```ts
// features/registration/services/patients.ts — I/O only
export const searchDuplicates = (q: DuplicateQuery) => api.post('/v1/patients/duplicate-check', q);

// features/registration/domain/patient.ts — pure rules
export const pinCodeValid = (pin: string) => /^\d{6}$/.test(pin);

// features/registration/hooks/useRegistrationController.ts — orchestration
export function useRegistrationController() {
  const form = useForm<Registration>({ resolver: zodResolver(registrationSchema) });
  const duplicates = useQuery({ queryKey: ['dup', form.watch('name'), form.watch('dob')], queryFn: () => searchDuplicates(...) });
  const save = useMutation({ mutationFn: registerPatient });
  return { form, duplicates: duplicates.data ?? [], saving: save.isPending, onSubmit: form.handleSubmit(v => save.mutate(v)) };
}

// features/registration/components/RegistrationForm.tsx — presentational
export default function RegistrationForm(vm: ReturnType<typeof useRegistrationController>) { … }

// app/ (route element)
const vm = useRegistrationController();
return <RegistrationForm {...vm} />;
```

Why it matters for Medvya:

- **Testability:** domain rules and services are tested without React; controllers with `renderHook`; components with plain props.
- **Safety review:** every place that reads or writes patient data is a service, so security review covers a small, findable surface.
- **Portability:** the patient portal and a future mobile app (Expo) reuse `domain/` and `services/` unchanged.

## Libraries (PRD v2 §07, ADR-001)

| Concern | Library | When it lands |
|---|---|---|
| Server state, caching, retries | **TanStack Query** | First API data: `/v1/me/context` (MDV-P0-012) |
| Forms and validation | **React Hook Form + Zod** (Zod schemas in `domain/`) | First form: registration (MDV-P1-013) |
| Routing | **React Router** (SPA) | App shell (MDV-P0-022) |
| API client | Generated from OpenAPI into `@medvya/api-client` | With the first endpoint used by the UI (MDV-P0-012) |
| UI | shadcn/ui + Radix + Tailwind 4 + Lucide via `@medvya/ui` (ADR-010) | In place |
| Tables, dates | TanStack Table, react-day-picker (shadcn calendar) | With the first table screen (P1) |
| Schedule grid | Own CSS grid in `features/schedule` (FullCalendar's doctor and resource columns are a paid plugin, and we need our own slot buttons, states and RTL) | MDV-P1-011 |

## Navigation and route guards (MDV-P0-022)

`features/shell/domain/navigation.ts` is the single map of screen → path → required capability (and access levels) → optional tenant feature flag → delivery phase. The shell renders, and the router registers, only the items `visibleNav()` returns for the active membership from `/v1/me/context`, so a hidden screen has no route (it shows "Page not found"). Screens after the R1 launch carry a flag and stay hidden until the tenant turns it on. This is a UX filter only: every API endpoint still enforces its own policy (AGENTS.md 1.3).

No other state library (Redux, Zustand…) unless a decision record justifies it. Server state belongs in TanStack Query, form state in React Hook Form, and the little UI state left in `useState`.
