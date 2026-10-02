"""Real-input production outline authoring, zero-extent line layout and rendering checks."""
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
    def field(name, tag):
        page.wait_for_function("""([name,tag]) => { const i=document.activeElement;
            return i?.id === 'uno-input' && i.tagName === tag && i.selectionStart === 0 && i.selectionEnd === i.value.length
              && (document.documentElement.getAttribute('data-focus-id') || '').startsWith(name + ':'); }""", arg=[name,tag], timeout=20000)
    def shot(name):
        page.evaluate('() => new Promise(r => requestAnimationFrame(() => requestAnimationFrame(r)))')
        page.screenshot(path=str(out/(name+'.png')))
    try:
        page.goto(url, wait_until='domcontentloaded', timeout=60000)
        page.wait_for_function("document.documentElement.getAttribute('data-presentationspace') === 'ready'", timeout=120000)
        page.bring_to_front(); attr('data-slide-count',4)
        page.wait_for_function("document.hasFocus() && document.documentElement.getAttribute('data-focus-id') === 'slide-canvas:SlideViewport'",timeout=20000)
        command('Open outline sample'); attr('data-slide-count',5); attr('data-slide-name','Outlines · dashes and arrows')
        attr('data-outline-width',5); attr('data-primary-height',0); shot('outline-layout-editor')
        page.keyboard.press('Shift+F5'); attr('data-presenting','true'); shot('outline-layout-show')
        page.keyboard.press('Escape'); attr('data-presenting','false')
        command('Outline dash LongDashDot'); attr('data-outline-dash','LongDashDot')
        command('Outline cap Square'); attr('data-outline-cap','Square')
        command('Outline join Bevel'); attr('data-outline-join','Bevel')
        command('Outline begin Diamond'); attr('data-outline-begin','Diamond')
        command('Outline end OpenArrow'); attr('data-outline-end','OpenArrow')
        command('Undo'); attr('data-outline-end','Triangle'); command('Redo'); attr('data-outline-end','OpenArrow')
        command('Reverse line direction'); attr('data-line-flip-h','true'); attr('data-line-flip-v','true')
        command('Undo'); attr('data-line-flip-h','false'); attr('data-line-flip-v','false')
        command('Outline gradient Ocean'); attr('data-outline-gradients',3)
        command('Edit outline'); field('outline-width','INPUT'); page.keyboard.insert_text('7.5'); page.keyboard.press('Enter')
        attr('data-outline-width','7.5'); attr('data-outline-gradients',3)
        command('Edit custom outline dashes'); field('outline-dashes','TEXTAREA')
        pairs='4 2\n0 1'; page.keyboard.insert_text(pairs); page.keyboard.press('Control+Enter'); attr('data-outline-custom',pairs)
        shot('outline-layout-pane')
        command('Edit custom outline dashes'); field('outline-dashes','TEXTAREA')
        page.keyboard.insert_text('0 0'); page.keyboard.press('Control+Enter'); attr('data-outline-custom',pairs)
        page.keyboard.press('Control+a'); page.keyboard.insert_text('6 2\n1 2'); page.keyboard.press('Control+Enter'); attr('data-outline-custom','6 2\n1 2')
        command('Edit outline gradient'); field('outline-gradient-stops','TEXTAREA')
        page.keyboard.insert_text('0 #FF0000 100\n100 #0000FF 100'); page.keyboard.press('Control+Enter'); attr('data-outline-gradients',2)
        command('Close format pane'); command('Slide size Standard'); attr('data-slide-width',960); attr('data-outline-width','5.625'); attr('data-outline-custom','6 2\n1 2')
        command('Undo'); attr('data-outline-width','7.5'); attr('data-slide-width',1280)
        page.set_viewport_size({'width':720,'height':620})
        page.wait_for_function("() => JSON.parse(document.documentElement.getAttribute('data-ui-chrome'))['title-bar'].width === 720")
        command('Edit outline'); field('outline-width','INPUT'); page.keyboard.insert_text('9'); page.keyboard.press('Enter'); attr('data-outline-width',9)
        attr('data-outline-end','OpenArrow'); attr('data-outline-gradients',2); shot('outline-layout-compact')
        command('Undo'); attr('data-outline-width','7.5'); command('Redo'); attr('data-outline-width',9)
        observations.append({'width':value('data-outline-width'),'dash':value('data-outline-custom'),'gradientStops':value('data-outline-gradients'),'lineHeight':value('data-primary-height')})
        assert not errors,errors
        print('PASS: native outline input, invalid drafts, caps/joins/ends, gradient strokes, direction, zero extents, sizing, compact authoring and undo/redo.',flush=True)
    except Exception:
        failures.append(traceback.format_exc()); raise
    finally:
        try:
            shot('outline-final')
            (out/'outline-state.json').write_text(json.dumps(page.evaluate("() => ({attributes:Object.fromEntries([...document.documentElement.attributes].filter(a=>a.name.startsWith('data-')).map(a=>[a.name,a.value])), input:{id:document.activeElement?.id,tag:document.activeElement?.tagName,value:document.activeElement?.value}})"),indent=2))
        except Exception as e: errors.append(str(e))
        (out/'browser-outlines.json').write_text(json.dumps({'errors':errors,'failures':failures,'observations':observations},indent=2))
        context.close(); browser.close()
