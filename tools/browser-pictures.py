"""Real-input picture crop/fit/mask/flip/transparency regressions against the production Uno app."""
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
    def crop(value):
        command('Edit picture layout')
        page.wait_for_function("""() => {const i=document.activeElement;return i?.id==='uno-input' && i.tagName==='INPUT'
          && i.selectionStart===0 && i.selectionEnd===i.value.length
          && (document.documentElement.getAttribute('data-focus-id')||'').startsWith('picture-crop-left:');}""", timeout=20000)
        page.keyboard.insert_text(value); page.keyboard.press('Enter')
    try:
        page.goto(url, wait_until='domcontentloaded', timeout=60000)
        page.wait_for_function("document.documentElement.getAttribute('data-presentationspace') === 'ready'", timeout=120000)
        page.bring_to_front(); attr('data-slide-count',4)
        page.wait_for_function("document.hasFocus() && document.documentElement.getAttribute('data-focus-id') === 'slide-canvas:SlideViewport'", timeout=20000)
        command('Open picture layout sample'); attr('data-slide-count',5); attr('data-slide-index',4); attr('data-picture-count',6)
        attr('data-picture-fit','Contain'); attr('data-ribbon-tab','Picture Format'); shot('picture-layout-editor')
        page.keyboard.press('Shift+F5'); attr('data-presenting','true'); shot('picture-layout-show'); page.keyboard.press('Escape'); attr('data-presenting','false')
        command('Picture fit Cover'); attr('data-picture-fit','Cover')
        command('Picture flip horizontal'); attr('data-picture-flip-h','true')
        command('Picture flip vertical'); attr('data-picture-flip-v','true')
        command('Picture mask Ellipse'); attr('data-picture-mask','Ellipse')
        command('Picture transparency 50'); attr('data-picture-opacity','0.5')
        command('Undo'); attr('data-picture-opacity','1'); command('Redo'); attr('data-picture-opacity','0.5')
        crop('25'); attr('data-picture-crop-left','0.25'); shot('picture-layout-pane')
        command('Undo'); attr('data-picture-crop-left','0'); command('Redo'); attr('data-picture-crop-left','0.25')
        crop('120'); attr('data-picture-crop-left','0.25')
        # Correct the same rejected draft; no partial crop or other formatting can have committed.
        page.wait_for_function("() => document.activeElement?.id === 'uno-input' && document.activeElement.tagName === 'INPUT'", timeout=20000)
        page.keyboard.press('Control+a'); page.keyboard.insert_text('15'); page.keyboard.press('Enter'); attr('data-picture-crop-left','0.15')
        command('Close format pane'); command('Slide size Standard'); attr('data-slide-width','960'); attr('data-picture-crop-left','0.15'); attr('data-picture-count',6)
        command('Undo'); attr('data-slide-width','1280'); attr('data-picture-crop-left','0.15')
        command('Picture reset crop'); attr('data-picture-crop-left','0'); attr('data-picture-fit','Contain'); attr('data-picture-mask','Ellipse')
        command('Undo'); attr('data-picture-crop-left','0.15'); attr('data-picture-fit','Cover')
        page.set_viewport_size({'width':720,'height':620})
        page.wait_for_function('() => JSON.parse(document.documentElement.getAttribute("data-ui-chrome"))["title-bar"].width === 720')
        crop('20'); attr('data-picture-crop-left','0.2'); shot('picture-layout-compact')
        command('Close format pane'); command('Picture fit Stretch'); attr('data-picture-fit','Stretch')
        observations.append({'pictureCount': value('data-picture-count'), 'cropLeft': value('data-picture-crop-left'), 'opacity': value('data-picture-opacity')})
        assert not errors, errors
        print('PASS: picture fit/crop, numeric draft rejection and correction, masks, flips, alpha, undo/redo, sizing, compact authoring and player.', flush=True)
    except Exception:
        failures.append(traceback.format_exc()); raise
    finally:
        try: shot('picture-layout-final')
        except Exception as e: errors.append(str(e))
        (out/'browser-pictures.json').write_text(json.dumps({'errors':errors,'failures':failures,'observations':observations},indent=2))
        context.close(); browser.close()
