"""Real keyboard input for custom tab definitions, style preservation, scaling and player rendering."""
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
    page = context.new_page(); page.on('pageerror', lambda error: errors.append(str(error)))
    def value(name): return page.locator('html').get_attribute(name)
    def attr(name, expected):
        page.wait_for_function('(a) => document.documentElement.getAttribute(a[0]) === a[1]', arg=[name, str(expected)], timeout=20000)
    def command(text):
        before = int(value('data-command-version')); page.keyboard.press('Alt+q')
        page.wait_for_function("() => document.activeElement?.id === 'uno-input' && document.activeElement.tagName === 'INPUT' && document.activeElement.value === ''", timeout=20000)
        page.keyboard.type(text); page.keyboard.press('Enter'); attr('data-command-version', before + 1)
    def shot(name):
        page.evaluate('() => new Promise(r => requestAnimationFrame(() => requestAnimationFrame(r)))')
        page.screenshot(path=str(out / (name + '.png')))
    def edit_tabs(text):
        command('Edit custom tab stops')
        page.wait_for_function("""() => { const i = document.activeElement;
            return i?.id === 'uno-input' && i.tagName === 'TEXTAREA' && i.selectionStart === 0 && i.selectionEnd === i.value.length
                && (document.documentElement.getAttribute('data-focus-id') || '').startsWith('text-layout-tabs:'); }""", timeout=20000)
        page.keyboard.insert_text(text); page.keyboard.press('Control+Enter')
    try:
        page.goto(url, wait_until='domcontentloaded', timeout=60000)
        page.wait_for_function("document.documentElement.getAttribute('data-presentationspace') === 'ready'", timeout=120000)
        command('Open custom tabs sample'); attr('data-slide-count', 5); attr('data-slide-index', 4)
        attr('data-slide-name', 'Custom tabs · aligned fields'); shot('custom-tabs-editor')
        page.keyboard.press('Shift+F5'); attr('data-presenting', 'true'); shot('custom-tabs-show')
        page.keyboard.press('Escape'); attr('data-presenting', 'false')
        command('New slide'); page.keyboard.press('Control+a'); page.keyboard.press('Delete'); attr('data-shape-count', 0)
        command('Insert text box')
        page.wait_for_function("() => document.activeElement?.tagName === 'TEXTAREA'", timeout=20000)
        text = 'Part A\t12.50\tReady\nPart B\t123.5\tPending'
        page.keyboard.insert_text(text); page.keyboard.press('Control+Home')
        for _ in range(6): page.keyboard.press('Shift+ArrowRight')
        page.wait_for_function('() => document.activeElement.selectionStart === 0 && document.activeElement.selectionEnd === 6')
        page.keyboard.press('Control+b'); attr('data-primary-range-length', 6)
        page.keyboard.press('Control+Enter')
        stops = '130 Decimal\n250 Right'
        edit_tabs(stops); attr('data-text-tab-stops', stops); attr('data-text-tab-count', 2)
        attr('data-primary-text-length', len(text)); attr('data-primary-range-length', 6)
        shot('custom-tabs-pane')
        command('Undo'); attr('data-text-tab-count', 0)
        command('Redo'); attr('data-text-tab-stops', stops)
        # Invalid ascending order must not commit anything; correcting it can be applied next.
        edit_tabs('250 Right\n130 Decimal'); attr('data-text-tab-stops', stops)
        page.wait_for_function("() => document.activeElement?.tagName === 'TEXTAREA'", timeout=20000)
        page.keyboard.press('Control+a'); page.keyboard.insert_text('140 Decimal\n270 Right'); page.keyboard.press('Control+Enter')
        attr('data-text-tab-stops', '140 Decimal\n270 Right')
        command('Close format pane'); command('Slide size Standard'); attr('data-slide-width', 960)
        attr('data-text-tab-stops', '105 Decimal\n202.5 Right')
        command('Undo'); attr('data-slide-width', 1280); attr('data-text-tab-stops', '140 Decimal\n270 Right')
        command('Clear custom tab stops'); attr('data-text-tab-count', 0)
        command('Undo'); attr('data-text-tab-count', 2)
        page.set_viewport_size({'width': 720, 'height': 620})
        page.wait_for_function('() => JSON.parse(document.documentElement.getAttribute("data-ui-chrome"))["title-bar"].width === 720')
        edit_tabs('135 Center\n280 Decimal'); attr('data-text-tab-stops', '135 Center\n280 Decimal')
        attr('data-primary-text-length', len(text)); shot('custom-tabs-compact')
        observations.append({'textLength': len(text), 'tabStops': value('data-text-tab-stops'), 'rangeLength': value('data-primary-range-length')})
        assert not errors, errors
        print('PASS: native custom-tab entry, invalid-order rejection, mixed styles, undo/redo, scaling, compact authoring and slide show.', flush=True)
    except Exception:
        failures.append(traceback.format_exc()); raise
    finally:
        try: shot('custom-tabs-final')
        except Exception as error: errors.append(str(error))
        (out / 'browser-tabs.json').write_text(json.dumps({'errors': errors, 'failures': failures, 'observations': observations}, indent=2))
        context.close(); browser.close()
