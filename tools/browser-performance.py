"""Real-input regression checks for direct cell editing and viewport-bounded large decks."""
import json
import os
import time
from pathlib import Path
from playwright.sync_api import sync_playwright

url = os.environ.get('PRESENTATIONSPACE_URL', 'http://127.0.0.1:8080/PresentationSpace/')
out = Path('artifacts/screenshots')
out.mkdir(parents=True, exist_ok=True)
with sync_playwright() as p:
    browser = p.chromium.launch(args=['--no-sandbox', '--use-gl=angle', '--use-angle=swiftshader', '--enable-unsafe-swiftshader'])
    errors = []
    observations = []
    def start():
        context = browser.new_context(viewport={'width': 1440, 'height': 1000})
        page = context.new_page()
        page.on('pageerror', lambda error: errors.append(str(error)))
        page.goto(url, wait_until='domcontentloaded', timeout=60000)
        page.wait_for_function("document.documentElement.getAttribute('data-presentationspace') === 'ready'", timeout=120000)
        return context, page
    def attr(name, value):
        page.wait_for_function('(a) => document.documentElement.getAttribute(a[0]) === a[1]', arg=[name, str(value)], timeout=20000)
    def value(name):
        return page.locator('html').get_attribute(name)
    def command(text):
        before = int(value('data-command-version'))
        page.keyboard.press('Alt+q')
        page.wait_for_function("() => document.activeElement?.id === 'uno-input' && document.activeElement.tagName === 'INPUT' && document.activeElement.value === ''", timeout=20000)
        page.keyboard.type(text)
        page.keyboard.press('Enter')
        attr('data-command-version', before + 1)
    def editing():
        page.wait_for_function("() => document.activeElement?.id === 'uno-input' && document.activeElement.tagName === 'TEXTAREA'", timeout=20000)
    def capture_state(name):
        page.screenshot(path=str(out / (name + '.png')), full_page=True)
        (out / (name + '.json')).write_text(json.dumps(page.evaluate("() => ({attributes:Object.fromEntries([...document.documentElement.attributes].filter(a=>a.name.startsWith('data-')).map(a=>[a.name,a.value])), input:{tag:document.activeElement?.tagName,value:document.activeElement?.value}})"), indent=2))
    context, page = start()
    try:
        attr('data-canvas-backend', 'SKCanvasElement')
        command('New slide')
        page.keyboard.press('Control+a')
        page.keyboard.press('Delete')
        command('Insert table')
        attr('data-table-origins', 12)
        before = int(value('data-table-text-length'))
        command('Edit table cell on slide')
        editing()
        old_length = page.evaluate('document.activeElement.value.length')
        page.keyboard.insert_text('Canvas heading\nSecond line')
        page.keyboard.press('Control+Enter')
        attr('data-table-text-length', before - old_length + len('Canvas heading\nSecond line'))
        attr('data-table-cell-row', 0)
        attr('data-table-cell-column', 0)
        page.keyboard.press('Control+z')
        attr('data-table-text-length', before)
        page.keyboard.press('Control+y')
        attr('data-table-text-length', before - old_length + len('Canvas heading\nSecond line'))
        page.keyboard.press('Tab')
        attr('data-table-cell-column', 1)
        page.keyboard.press('Enter')
        editing()
        page.keyboard.insert_text('Second cell')
        page.keyboard.press('Tab')
        editing()
        attr('data-table-cell-column', 2)
        # Double tap the third cell after cancelling its native input overlay.
        rect = page.evaluate("() => {const r=document.activeElement.getBoundingClientRect();return {x:r.x+r.width/2,y:r.y+r.height/2};}")
        page.keyboard.press('Escape')
        page.mouse.dblclick(rect['x'], rect['y'])
        editing()
        attr('data-table-cell-column', 2)
        page.keyboard.press('Escape')
        page.keyboard.press('ArrowLeft')
        page.keyboard.press('ArrowLeft')
        attr('data-table-cell-column', 0)
        page.keyboard.press('Enter')
        editing()
        page.keyboard.insert_text(' '.join(['Automatic row height'] * 25))
        page.keyboard.press('Control+Enter')
        height = float(value('data-primary-height'))
        command('Auto-fit table rows')
        page.wait_for_function('(h) => Number(document.documentElement.getAttribute("data-primary-height")) > h', arg=height)
        fitted = value('data-primary-height')
        page.keyboard.press('Control+z')
        assert abs(float(value('data-primary-height')) - height) < .01
        page.keyboard.press('Control+y')
        attr('data-primary-height', fitted)
        capture_state('table-canvas-autofit')
        print('PASS: on-slide cell input, Tab navigation, pointer double-tap, undo/redo and content-driven row sizing.', flush=True)
    finally:
        capture_state('canvas-final')
        context.close()
    context, page = start()
    try:
        command('Open large-deck sample')
        attr('data-slide-count', 1000)
        attr('data-slide-index', 0)
        page.wait_for_function('Number(document.documentElement.getAttribute("data-filmstrip-realized")) > 0')
        def bounded():
            counts = {key: int(value('data-' + key)) for key in ['filmstrip-realized', 'filmstrip-allocated', 'sorter-realized', 'sorter-allocated']}
            assert 0 < counts['filmstrip-realized'] < 25, counts
            assert counts['filmstrip-allocated'] < 50, counts
            assert counts['sorter-realized'] < 100 and counts['sorter-allocated'] < 200, counts
            return counts
        observations.append({'view': 'filmstrip-start', **bounded()})
        command('Focus slide thumbnails')
        begin = time.perf_counter()
        page.keyboard.press('End')
        attr('data-slide-index', 999)
        observations.append({'view': 'filmstrip-end', 'inputToModelMilliseconds': (time.perf_counter() - begin) * 1000, **bounded()})
        page.keyboard.press('Home')
        attr('data-slide-index', 0)
        command('Focus slide sorter')
        page.wait_for_function('Number(document.documentElement.getAttribute("data-sorter-realized")) > 0')
        page.keyboard.press('End')
        attr('data-slide-index', 999)
        observations.append({'view': 'sorter-end', **bounded()})
        capture_state('large-deck-sorter')
        page.keyboard.press('Home')
        attr('data-slide-index', 0)
        page.keyboard.press('PageDown')
        page.wait_for_function('Number(document.documentElement.getAttribute("data-slide-index")) > 0')
        assert int(value('data-slide-index')) < 50
        page.set_viewport_size({'width': 1024, 'height': 768})
        page.wait_for_timeout(300)
        observations.append({'view': 'compact', **bounded()})
        capture_state('large-deck-compact')
        page.set_viewport_size({'width': 1440, 'height': 1000})
        page.wait_for_timeout(300)
        command('Normal view')
        command('Focus slide thumbnails')
        page.keyboard.press('End')
        attr('data-slide-index', 999)
        page.keyboard.press('Delete')
        attr('data-slide-count', 999)
        command('Undo')
        attr('data-slide-count', 1000)
        print('PASS: 1,000-slide filmstrip/sorter navigation, bounded tile realization, resize, deletion and undo.', flush=True)
        assert not errors, errors
    finally:
        capture_state('performance-final')
        (out / 'browser-performance.json').write_text(json.dumps({'note': 'Synthetic browser sample and input/model latency, not physical-GPU FPS.', 'observations': observations, 'errors': errors}, indent=2))
        context.close()
        browser.close()
