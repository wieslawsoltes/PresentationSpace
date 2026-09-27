"""Exercise the published Uno application with real Chromium keyboard input."""
import json
import os
from pathlib import Path
from playwright.sync_api import sync_playwright

output = Path('artifacts/screenshots')
output.mkdir(parents=True, exist_ok=True)
url = os.environ.get('PRESENTATIONSPACE_URL', 'http://127.0.0.1:8080/PresentationSpace/')
errors, console = [], []

with sync_playwright() as playwright:
    browser = playwright.chromium.launch(headless=True, args=['--no-sandbox', '--enable-unsafe-swiftshader', '--use-angle=swiftshader'])
    context = browser.new_context(viewport={'width': 1440, 'height': 960}, device_scale_factor=1)
    page = context.new_page()
    page.on('pageerror', lambda error: errors.append(str(error)))
    page.on('console', lambda message: console.append(f'{message.type}: {message.text}'))
    def attr(name, value):
        page.wait_for_function('(args) => document.documentElement.getAttribute(args[0]) === args[1]', arg=[name, str(value)], timeout=15000)
    try:
        page.goto(url, wait_until='domcontentloaded', timeout=60000)
        page.wait_for_function("document.documentElement.getAttribute('data-presentationspace') === 'ready'", timeout=120000)
        attr('data-slide-count', 4)
        page.evaluate('() => new Promise(resolve => requestAnimationFrame(() => requestAnimationFrame(resolve)))')
        page.screenshot(path=str(output / 'editor.png'), full_page=True)
        assert page.locator('canvas').count() > 0, 'No Skia canvas was created.'
        page.keyboard.press('Control+m')
        attr('data-slide-count', 5)
        page.keyboard.press('Control+z')
        attr('data-slide-count', 4)
        page.keyboard.press('Control+y')
        attr('data-slide-count', 5)
        page.keyboard.press('Alt+q')
        page.keyboard.type('Insert rectangle')
        page.keyboard.press('Enter')
        attr('data-selection-count', 1)
        count = int(page.locator('html').get_attribute('data-shape-count'))
        page.keyboard.press('ArrowRight')
        page.keyboard.press('Control+d')
        attr('data-shape-count', count + 1)
        page.keyboard.press('Control+z')
        attr('data-shape-count', count)
        page.screenshot(path=str(output / 'editing.png'), full_page=True)
        page.keyboard.press('F5')
        attr('data-presenting', 'true')
        page.keyboard.press('ArrowRight')
        page.screenshot(path=str(output / 'slide-show.png'), full_page=True)
        page.keyboard.press('Escape')
        attr('data-presenting', 'false')
        page.set_viewport_size({'width': 1024, 'height': 768})
        page.evaluate('() => new Promise(resolve => requestAnimationFrame(() => requestAnimationFrame(resolve)))')
        page.screenshot(path=str(output / 'compact.png'), full_page=True)
        assert not errors, '\n'.join(errors)
        print('PASS: startup, Skia canvas, new slide, undo/redo, command search, shape duplication, slideshow and compact viewport.')
    finally:
        page.screenshot(path=str(output / 'final-state.png'), full_page=True)
        (output / 'console.log').write_text('\n'.join(console), encoding='utf-8')
        (output / 'errors.json').write_text(json.dumps(errors, indent=2), encoding='utf-8')
        (output / 'dom.html').write_text(page.content(), encoding='utf-8')
        browser.close()
