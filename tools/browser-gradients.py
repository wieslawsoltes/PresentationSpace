"""Production native input and rendering checks for editable linear gradient fills."""
import json
import os
import traceback
from pathlib import Path
from playwright.sync_api import sync_playwright

out = Path('artifacts/screenshots'); out.mkdir(parents=True, exist_ok=True)
url = os.environ.get('PRESENTATIONSPACE_URL', 'http://127.0.0.1:8080/PresentationSpace/')
errors, failures, observations = [], [], []
with sync_playwright() as p:
    browser = p.chromium.launch(args=['--no-sandbox', '--use-angle=swiftshader', '--enable-unsafe-swiftshader'])
    context = browser.new_context(viewport={'width': 1440, 'height': 1000})
    page = context.new_page(); page.on('pageerror', lambda e: errors.append(str(e)))
    def value(key): return page.locator('html').get_attribute(key)
    def attr(key, expected):
        page.wait_for_function('(a) => document.documentElement.getAttribute(a[0]) === a[1]', arg=[key, str(expected)], timeout=20000)
    def command(text):
        before = int(value('data-command-version')); page.keyboard.press('Alt+q')
        page.wait_for_function("() => document.activeElement?.id === 'uno-input' && document.activeElement.tagName === 'INPUT' && document.activeElement.value === ''", timeout=20000)
        page.keyboard.type(text); page.keyboard.press('Enter'); attr('data-command-version', before + 1)
    def resize(width, height):
        page.set_viewport_size({'width': width, 'height': height})
        # Playwright's viewport change is not an Uno layout completion fence.
        # Do not send Alt+Q while the old compact search host is being collapsed.
        # Wait for the arranged workspace and responsive search-host selection,
        # without setting focus, retrying the command, or relaxing its assertion.
        page.wait_for_function("""([width, height]) => {
            const raw = document.documentElement.getAttribute('data-ui-chrome');
            if (!raw) return false;
            const ui = JSON.parse(raw), title = ui['title-bar'], workspace = ui['workspace'];
            const status = ui['status-bar'], search = ui['command-search'], more = ui['quick-more'];
            return Math.abs(title.width - width) < 1 && Math.abs(workspace.width - width) < 1
                && status.height > 0 && Math.abs(status.y + status.height - height) < 1
                && search.visible === (width >= 1180) && more.visible === (width < 1180)
                && (width < 1180 || (search.width > 0 && search.x + search.width <= width));
        }""", arg=[width, height], timeout=20000)
    def shot(name):
        page.evaluate('() => new Promise(r => requestAnimationFrame(() => requestAnimationFrame(r)))')
        page.screenshot(path=str(out / (name + '.png')))
    def input_field(name, tag):
        page.wait_for_function("""([name, tag]) => {const i = document.activeElement;
          return i?.id === 'uno-input' && i.tagName === tag && i.selectionStart === 0 && i.selectionEnd === i.value.length
            && (document.documentElement.getAttribute('data-focus-id') || '').startsWith(name + ':'); }""", arg=[name, tag], timeout=20000)
    def stops(text, opener='Edit gradient fill'):
        command(opener); input_field('gradient-stops', 'TEXTAREA')
        page.keyboard.insert_text(text); page.keyboard.press('Control+Enter')
    try:
        page.goto(url, wait_until='domcontentloaded', timeout=60000)
        page.wait_for_function("document.documentElement.getAttribute('data-presentationspace') === 'ready'", timeout=120000)
        page.bring_to_front(); attr('data-slide-count', 4)
        page.wait_for_function("document.hasFocus() && document.documentElement.getAttribute('data-focus-id') === 'slide-canvas:SlideViewport'", timeout=20000)
        command('Open gradient fill sample'); attr('data-slide-count', 5); attr('data-slide-index', 4)
        attr('data-gradient-count', 3); attr('data-background-gradient', 'true'); shot('gradient-layout-editor')
        page.keyboard.press('Shift+F5'); attr('data-presenting', 'true'); shot('gradient-layout-show')
        page.keyboard.press('Escape'); attr('data-presenting', 'false')
        command('Gradient fill Violet'); attr('data-gradient-angle', 135)
        command('Undo'); attr('data-gradient-angle', 45)
        command('Redo'); attr('data-gradient-angle', 135)
        custom = '0 #FF0000 100\n50 #FFFF00 70\n100 #0000FF 100'
        stops(custom); attr('data-gradient-stops', custom); shot('gradient-layout-pane')
        # An invalid draft must not alter any committed stop; correction stays usable.
        stops('75 #FF0000\n25 #0000FF'); attr('data-gradient-stops', custom)
        page.keyboard.press('Control+a'); page.keyboard.insert_text(custom); page.keyboard.press('Control+Enter')
        attr('data-gradient-stops', custom)
        command('Edit gradient angle'); input_field('gradient-angle', 'INPUT')
        page.keyboard.insert_text('72.5'); page.keyboard.press('Enter'); attr('data-gradient-angle', '72.5')
        command('Undo'); attr('data-gradient-angle', 135)
        command('Redo'); attr('data-gradient-angle', '72.5')
        command('Remove gradient fill'); attr('data-gradient-count', 0)
        command('Undo'); attr('data-gradient-count', 3)
        command('Close format pane'); command('Slide size Standard'); attr('data-slide-width', 960); attr('data-gradient-stops', custom)
        command('Undo'); attr('data-slide-width', 1280)
        resize(720, 620)
        compact = '0 #157F9C 100\n100 #D9BEED 40'
        stops(compact); attr('data-gradient-stops', compact); shot('gradient-layout-compact')
        background = '0 #071D32 100\n100 #26365C 100'
        stops(background, 'Edit background gradient'); attr('data-selection-count', 0)
        attr('data-gradient-stops', background); attr('data-background-gradient', 'true')
        command('Remove background gradient'); attr('data-background-gradient', 'false')
        command('Undo'); attr('data-background-gradient', 'true'); attr('data-gradient-stops', background)
        command('Close format pane'); resize(1440, 1000)
        command('New slide'); page.keyboard.press('Control+a'); page.keyboard.press('Delete'); attr('data-shape-count', 0)
        command('Insert table'); command('Table select all'); command('Table gradient Ocean'); attr('data-table-gradients', 12)
        command('Undo'); attr('data-table-gradients', 0)
        command('Redo'); attr('data-table-gradients', 12)
        stops(custom, 'Edit table gradient'); attr('data-table-gradients', 12)
        shot('gradient-table-pane')
        command('Close format pane'); page.keyboard.press('Shift+F5'); attr('data-presenting', 'true'); shot('gradient-table-show')
        page.keyboard.press('Escape'); attr('data-presenting', 'false')
        # Cross the exact search-host breakpoint in both directions. Every
        # command is issued once and still requires an empty focused native input.
        command('Close format pane')
        for width in [720, 1180, 1179, 1440, 720, 1440]:
            resize(width, 620 if width < 1180 else 1000)
            version = int(value('data-command-version'))
            command('Show Home ribbon'); attr('data-ribbon-tab', 'Home')
            command('Show Shape Format ribbon'); attr('data-ribbon-tab', 'Shape Format')
            assert int(value('data-command-version')) == version + 2
            attr('data-table-gradients', 12)
            observations.append({'width': width, 'commandVersion': version + 2, 'gradientCells': value('data-table-gradients')})
        observations.append({'slideCount': value('data-slide-count'), 'gradientCells': value('data-table-gradients')})
        assert not errors, errors
        print('PASS: gradient presets, actual stop/angle entry, invalid drafts, backgrounds/cells, undo/redo, scaling, compact authoring and repeated responsive search transitions.', flush=True)
    except Exception:
        failures.append(traceback.format_exc()); raise
    finally:
        try:
            shot('gradient-final')
            state = page.evaluate("() => ({attributes:Object.fromEntries([...document.documentElement.attributes].filter(a=>a.name.startsWith('data-')).map(a=>[a.name,a.value])),input:{id:document.activeElement?.id,tag:document.activeElement?.tagName,value:document.activeElement?.value}})")
            (out / 'gradient-state.json').write_text(json.dumps(state, indent=2))
        except Exception as e: errors.append(str(e))
        (out / 'browser-gradients.json').write_text(json.dumps({'errors': errors, 'failures': failures, 'observations': observations}, indent=2))
        context.close(); browser.close()
