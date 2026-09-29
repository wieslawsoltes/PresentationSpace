"""Real-keyboard text-fit, sample, slide-size and presentation regressions on the published Uno app."""
import json
import os
import traceback
from pathlib import Path
from playwright.sync_api import sync_playwright

out = Path('artifacts/screenshots')
out.mkdir(parents=True, exist_ok=True)
errors, failures, observations = [], [], []
url = os.environ.get('PRESENTATIONSPACE_URL', 'http://127.0.0.1:8080/PresentationSpace/')
with sync_playwright() as p:
    browser = p.chromium.launch(args=['--no-sandbox', '--use-angle=swiftshader', '--enable-unsafe-swiftshader'])
    context = browser.new_context(viewport={'width': 1440, 'height': 1000})
    page = context.new_page()
    page.on('pageerror', lambda error: errors.append(str(error)))
    def value(name):
        return page.locator('html').get_attribute(name)
    def attr(name, expected):
        page.wait_for_function('(a) => document.documentElement.getAttribute(a[0]) === a[1]', arg=[name, str(expected)], timeout=20000)
    def frames():
        page.evaluate('() => new Promise(resolve => requestAnimationFrame(() => requestAnimationFrame(resolve)))')
    def command(text):
        before = int(value('data-command-version'))
        page.keyboard.press('Alt+q')
        page.wait_for_function("() => document.activeElement?.id === 'uno-input' && document.activeElement.tagName === 'INPUT' && document.activeElement.value === ''", timeout=20000)
        page.keyboard.type(text)
        page.keyboard.press('Enter')
        attr('data-command-version', before + 1)
    def font_label(expected):
        page.wait_for_function("""expected => {
            const label = document.documentElement.getAttribute('data-ribbon-font-size');
            return label && Math.abs(Number(label) - expected) <= .011;
        }""", arg=expected, timeout=20000)
    def screenshot(name):
        frames()
        page.screenshot(path=str(out / (name + '.png')))
    try:
        page.goto(url, wait_until='domcontentloaded', timeout=60000)
        page.wait_for_function("document.documentElement.getAttribute('data-presentationspace') === 'ready'", timeout=120000)
        attr('data-slide-count', 4)
        command('Open typography sample')
        attr('data-slide-count', 6)
        attr('data-slide-index', 4)
        attr('data-slide-name', 'Typography · shared baselines')
        screenshot('typography-baselines')
        page.keyboard.press('Shift+F5')
        attr('data-presenting', 'true')
        screenshot('typography-slide-show')
        page.keyboard.press('ArrowRight')
        screenshot('typography-table-slide-show')
        page.keyboard.press('Escape')
        attr('data-presenting', 'false')
        command('Slide sorter')
        screenshot('typography-sorter')
        command('Normal view')
        command('New slide')
        attr('data-slide-count', 7)
        page.keyboard.press('Control+a'); page.keyboard.press('Delete')
        attr('data-shape-count', 0)
        command('Insert text box')
        page.wait_for_function("() => document.activeElement?.id === 'uno-input' && document.activeElement.tagName === 'TEXTAREA'", timeout=20000)
        text = 'Mixed text stays aligned and wraps within the shape. ' * 8
        page.keyboard.insert_text(text)
        page.keyboard.press('Control+Home')
        for _ in range(5):
            page.keyboard.press('Shift+ArrowRight')
        page.wait_for_function('() => document.activeElement.selectionStart === 0 && document.activeElement.selectionEnd === 5')
        page.keyboard.press('Control+b')
        attr('data-primary-text-length', len(text))
        attr('data-primary-range-count', 1)
        page.keyboard.press('Control+Enter')
        page.wait_for_function("() => document.activeElement?.tagName !== 'TEXTAREA'")
        size = float(value('data-primary-font-size'))
        height = float(value('data-primary-height'))
        command('Shrink text to fit')
        page.wait_for_function('(size) => Number(document.documentElement.getAttribute("data-primary-font-size")) < size', arg=size)
        fitted = float(value('data-primary-font-size'))
        attr('data-primary-text-length', len(text))
        attr('data-primary-range-start', 0); attr('data-primary-range-length', 5)
        font_label(fitted)
        command('Show Shape Format ribbon'); command('Show Home ribbon')
        font_label(fitted)
        assert abs(float(value('data-primary-font-size')) - fitted) < .001
        screenshot('typography-shrink-fit')
        command('Undo'); attr('data-primary-font-size', str(int(size))); font_label(size)
        command('Redo')
        assert abs(float(value('data-primary-font-size')) - fitted) < .001
        font_label(fitted)
        command('Undo')
        command('Resize shape to text')
        page.wait_for_function('(height) => Number(document.documentElement.getAttribute("data-primary-height")) > height', arg=height)
        expanded = float(value('data-primary-height'))
        attr('data-primary-font-size', str(int(size)))
        screenshot('typography-shape-fit')
        command('Undo'); assert abs(float(value('data-primary-height')) - height) < .001
        command('Slide size Standard')
        attr('data-slide-width', 960); attr('data-slide-height', 720)
        assert abs(float(value('data-primary-font-size')) - size * .75) < .001
        assert abs(float(value('data-primary-first-run-size')) - size * .75) < .001
        font_label(size * .75)
        attr('data-primary-text-length', len(text))
        command('Undo'); attr('data-slide-width', 1280)
        attr('data-primary-font-size', str(int(size)))
        observations.append({'sourceFontSize': size, 'fittedFontSize': fitted, 'sourceHeight': height, 'expandedHeight': expanded, 'textLength': len(text)})
        # Compact UI retains access to the new commands through Alt+Q.
        page.set_viewport_size({'width': 720, 'height': 600})
        page.wait_for_function('() => JSON.parse(document.documentElement.getAttribute("data-ui-chrome"))["title-bar"].width === 720')
        command('Shrink text to fit')
        assert float(value('data-primary-font-size')) < size
        font_label(float(value('data-primary-font-size')))
        screenshot('typography-compact')
        assert not errors, errors
        print('PASS: editable typography samples, slide show/sorter, text-fit commands, Ctrl+Enter, mixed-style preservation, slide resizing and undo/redo.', flush=True)
    except Exception:
        failures.append(traceback.format_exc())
        raise
    finally:
        try:
            screenshot('typography-final')
            observations.append(page.evaluate("() => Object.fromEntries([...document.documentElement.attributes].filter(a => a.name.startsWith('data-')).map(a => [a.name,a.value]))"))
        finally:
            (out / 'browser-typography.json').write_text(json.dumps({'observations': observations, 'errors': errors, 'failures': failures}, indent=2))
            context.close(); browser.close()
