"""Fails when a Svelte file uses a component in its markup that it never imported.

Worth having as its own check because `svelte-check` does NOT catch it: an unimported `<FooIcon />`
type-checks clean and compiles clean, then throws `ReferenceError: FooIcon is not defined` at
runtime — and because the throw happens inside Svelte's render, it takes the REST of that render
pass with it. The window comes up looking half-built and frozen, with nothing in the build log.

That shipped once (the Read PDF rail button, 2026-08-25). Runs in a second, needs no browser.

    python tools/uitest/check_components.py
"""
import pathlib
import re
import sys

UI = pathlib.Path(__file__).resolve().parents[2] / 'src' / 'Physalia.UI' / 'src'

# <svelte:*> are language constructs, not components.
SVELTE_BUILTINS = re.compile(r'^svelte:')


def declared_names(script):
    """Every identifier the script block brings into scope.

    Deliberately generous — a false negative here only costs a missed check, while a false positive
    would make the whole thing noise nobody runs. Handles multi-line `import { A, B } from '…'`,
    which a naive line-wise scan gets wrong.
    """
    names = set()
    for block in re.findall(r'import\s*\{(.*?)\}\s*from', script, re.S):
        for part in block.split(','):
            part = part.strip()
            if not part:
                continue
            names.add(part.split()[-1])          # handles "X as Y"
    names.update(re.findall(r'import\s+([A-Za-z_$][\w$]*)\s*(?:,|from)', script))
    # Namespace imports: `import * as Collapsible from '…'`, used as <Collapsible.Root>.
    names.update(re.findall(r'import\s*\*\s*as\s+([A-Za-z_$][\w$]*)', script))
    names.update(re.findall(r'\b(?:const|let|var|function|class)\s+([A-Za-z_$][\w$]*)', script))
    # Destructured props: let { a, b } = $props()
    for block in re.findall(r'\b(?:const|let|var)\s*\{(.*?)\}\s*=', script, re.S):
        for part in block.split(','):
            part = part.strip().split('=')[0].strip().split(':')[-1].strip()
            if part:
                names.add(part)
    return names


def main():
    problems = []
    files = sorted(UI.rglob('*.svelte'))
    for path in files:
        src = path.read_text(encoding='utf-8')
        script = '\n'.join(re.findall(r'<script[^>]*>(.*?)</script>', src, re.S))
        markup = re.sub(r'<script[^>]*>.*?</script>', '', src, flags=re.S)
        markup = re.sub(r'<!--.*?-->', '', markup, flags=re.S)

        known = declared_names(script)
        # {#snippet name(...)} defines a renderable too.
        known.update(re.findall(r'\{#snippet\s+([A-Za-z_$][\w$]*)', src))
        # {@const Icon = item.icon} makes a component available for the rest of that block.
        known.update(re.findall(r'\{@const\s+([A-Za-z_$][\w$]*)', src))

        for used in sorted(set(re.findall(r'<([A-Z][A-Za-z0-9_]*)', markup))):
            if SVELTE_BUILTINS.match(used) or used in known:
                continue
            problems.append(f'{path.relative_to(UI)}: <{used}> is used but never imported')

    for line in problems:
        print('UNDECLARED  ' + line)

    print(f'checked {len(files)} files, {len(problems)} problem(s)')
    return 1 if problems else 0


if __name__ == '__main__':
    sys.exit(main())
