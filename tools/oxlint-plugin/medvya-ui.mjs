// Medvya's own oxlint rules (jsPlugins): no height patch on a library control (MDV-P1-078); compact tables only where allowed (MDV-P2-054);
// no local Field in a feature (MDV-P2-058); no inline card and no local Message, Card or Empty in a feature (MDV-P2-059).
const CONTROLS = new Set(['Input', 'SelectTrigger', 'Textarea', 'Button']);
const PATCH = /(?<![\w:-])(?:min-)?h-11(?![\w-])/;

// Every string in the className expression: literals, template text and the strings inside cn(...) or a condition.
function strings(node, out = []) {
  if (!node || typeof node !== 'object') return out;
  if (node.type === 'Literal' && typeof node.value === 'string') out.push(node.value);
  else if (node.type === 'TemplateElement') out.push(node.value.cooked ?? node.value.raw);
  for (const [key, child] of Object.entries(node)) {
    if (key === 'parent') continue;
    if (Array.isArray(child)) child.forEach(c => strings(c, out));
    else if (child && typeof child === 'object' && typeof child.type === 'string') strings(child, out);
  }
  return out;
}

// The library component a feature uses instead of declaring its own (MDV-P2-059).
const SURFACES = { Message: 'PageMessage', Card: 'Card (or Section inside a Card)', Empty: 'EmptyState' };
const INLINE_CARD = /\bborder bg-card\b/;

const attr = (node, name) => node.attributes.find(a => a.type === 'JSXAttribute' && a.name.name === name);

export default {
  meta: { name: 'medvya-ui' },
  rules: {
    'no-height-patch': {
      meta: { type: 'problem', docs: { description: 'A library control is 44 px by default; a call site must not patch its height.' } },
      create(context) {
        return {
          JSXOpeningElement(node) {
            const name = node.name.type === 'JSXIdentifier' ? node.name.name : null;
            if (!CONTROLS.has(name)) return;
            const cls = attr(node, 'className');
            if (cls && strings(cls.value).some(s => PATCH.test(s)))
              context.report({ node: cls, message: `${name} is 44 px by default in @medvya/ui (library default, MDV-P1-077): remove h-11 / min-h-11 here (DESIGN.md, Density).` });
          },
        };
      },
    },
    // On everywhere; .oxlintrc.json turns it off for the four tables allowed to be compact, so a new compact table fails lint.
    'no-local-field': {
      meta: { type: 'problem', docs: { description: 'A feature uses Field from @medvya/ui and never declares its own.' } },
      create(context) {
        const report = node => context.report({ node, message: 'Use Field (and ErrorSummary) from @medvya/ui; a feature never builds its own Field (DESIGN.md Forms, MDV-P2-058).' });
        return {
          FunctionDeclaration(node) { if (node.id?.name === 'Field') report(node.id); },
          VariableDeclarator(node) { if (node.id.type === 'Identifier' && node.id.name === 'Field') report(node.id); },
        };
      },
    },
    'no-local-surface': {
      meta: { type: 'problem', docs: { description: 'A feature uses PageMessage, Card and EmptyState from @medvya/ui and never declares its own Message, Card or Empty.' } },
      create(context) {
        const check = id => {
          if (id && Object.hasOwn(SURFACES, id.name))
            context.report({ node: id, message: `Use ${SURFACES[id.name]} from @medvya/ui; a feature never builds its own ${id.name} (DESIGN.md, MDV-P2-059).` });
        };
        return {
          FunctionDeclaration(node) { check(node.id); },
          VariableDeclarator(node) { if (node.id.type === 'Identifier') check(node.id); },
        };
      },
    },
    'no-inline-card': {
      meta: { type: 'problem', docs: { description: 'A card surface is Card or Section from @medvya/ui, never a hand-written "border bg-card".' } },
      create(context) {
        const report = node => context.report({ node, message: 'A card is Card or Section from @medvya/ui, not "border bg-card" by hand (DESIGN.md, MDV-P2-059). A control that only looks outlined uses buttonVariants({ variant: \'outline\' }).' });
        return {
          Literal(node) { if (typeof node.value === 'string' && INLINE_CARD.test(node.value)) report(node); },
          TemplateElement(node) { if (INLINE_CARD.test(node.value.cooked ?? node.value.raw)) report(node); },
        };
      },
    },
    'compact-table': {
      meta: { type: 'problem', docs: { description: 'Compact density (36 px rows) is allowed on four tables only: audit log, formulary, order sets, staff.' } },
      create(context) {
        return {
          JSXOpeningElement(node) {
            const density = node.name.type === 'JSXIdentifier' && node.name.name === 'Table' && attr(node, 'density');
            if (density)
              context.report({ node: density, message: 'Compact density is allowed only on the audit log, formulary, order sets and staff tables (AGENTS.md section 4, DESIGN.md Density, MDV-P2-054). Remove density here.' });
          },
        };
      },
    },
  },
};
