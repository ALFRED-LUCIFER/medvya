// Medvya's own oxlint rules (jsPlugins). One rule today: no height patch on a library control (MDV-P1-078).
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
  },
};
