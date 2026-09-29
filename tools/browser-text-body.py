"""Real-input paragraph/body editing and presentation checks on the production Uno app."""
import json
import os
import traceback
from pathlib import Path
from playwright.sync_api import sync_playwright

out = Path('artifacts/screenshots')
out.mkdir(parents=True, exist_ok=True)
url = os.environ.get('PRESENTATIONSPACE_URL', 'http://127.0.0.1:8080/PresentationSpace/')
errors, failures, observations = [], [], []
with sync_playwright() as p:
    browser = p.chromium.launch(args=['--no-sandbox', '--use-angle=swiftshader', '--enable-unsafe-swiftshader'])
    context = browser.new_context(viewport={'width': 1440, 'height': 1000})
    page = context.new_page()
    page.on('pageerror', lambda error: errors.append(str(error)))
    def value(name):
        return page.locator('html').get_attribute(name)
    def attr(name, expected):
        page.wait_for_function('(a) => document.documentElement.getAttribute(a[0]) === a[1]', arg=[name, str(expected)], timeout=20000)
    def command(text):
        before = int(value('data-command-version'))
        page.keyboard.press('Alt+q')
        page.wait_for_function("() => document.activeElement?.id === 'uno-input' && document.activeElement.tagName === 'INPUT' && document.activeElement.value === ''", timeout=20000)
        page.keyboard.type(text); page.keyboard.press('Enter')
        attr('data-command-version', before + 1)
    def shot(name):
        page.evaluate('() => new Promise(r => requestAnimationFrame(() => requestAnimationFrame(r)))')
        page.screenshot(path=str(out / (name + '.png')))
    def edit_left_margin(amount):
        command('Edit text layout')
        page.wait_for_function("""() => {
            const input = document.activeElement;
            return input?.id === 'uno-input' && input.tagName === 'INPUT' && input.selectionStart === 0 && input.selectionEnd === input.value.length
                && (document.documentElement.getAttribute('data-focus-id') || '').startsWith('text-layout-left:');
        }""", timeout=20000)
        page.keyboard.insert_text(str(amount)); page.keyboard.press('Enter')
        attr('data-text-margin-left', amount)
    try:
        page.goto(url, wait_until='domcontentloaded', timeout=60000)
        page.wait_for_function("document.documentElement.getAttribute('data-presentationspace') === 'ready'", timeout=120000)
        attr('data-slide-count', 4)
        command('Open paragraph layout sample')
        attr('data-slide-count', 5); attr('data-slide-index', 4)
        attr('data-slide-name', 'Paragraph layout · margins and spacing')
        shot('paragraph-layout-editor')
        page.keyboard.press('Shift+F5'); attr('data-presenting', 'true')
        shot('paragraph-layout-slide-show')
        page.keyboard.press('Escape'); attr('data-presenting', 'false')
        command('New slide'); attr('data-slide-count', 6)
        page.keyboard.press('Control+a'); page.keyboard.press('Delete'); attr('data-shape-count', 0)
        command('Insert text box')
        page.wait_for_function("() => document.activeElement?.id === 'uno-input' && document.activeElement.tagName === 'TEXTAREA'", timeout=20000)
        text = 'Paragraph formatting retains mixed character styles and separate text margins. ' * 4
        page.keyboard.insert_text(text)
        page.keyboard.press('Control+Home')
        for _ in range(9):
            page.keyboard.press('Shift+ArrowRight')
        page.wait_for_function('() => document.activeElement.selectionStart === 0 && document.activeElement.selectionEnd === 9')
        page.keyboard.press('Control+b'); attr('data-primary-range-length', 9)
        page.keyboard.press('Control+Enter')
        page.wait_for_function("() => document.activeElement?.tagName !== 'TEXTAREA'")
        command('Text margins 12'); attr('data-text-margin-left', 12)
        command('Text wrap off'); attr('data-text-wrap', 'false')
        command('Undo'); attr('data-text-wrap', 'true')
        command('Redo'); attr('data-text-wrap', 'false')
        command('Text wrap on'); attr('data-text-wrap', 'true')
        command('Paragraph align Justify'); attr('data-paragraph-align', 'Justify')
        command('Paragraph space before 12'); attr('data-paragraph-before', 12)
        command('Paragraph space after 24'); attr('data-paragraph-after', 24)
        command('Undo'); attr('data-paragraph-after', 0)
        command('Redo'); attr('data-paragraph-after', 24)
        # Exercise the reusable pane's real native numeric input, not just commands.
        edit_left_margin(24)
        attr('data-primary-text-length', len(text))
        attr('data-primary-range-start', 0); attr('data-primary-range-length', 9)
        shot('paragraph-layout-pane')
        command('Undo'); attr('data-text-margin-left', 12)
        command('Redo'); attr('data-text-margin-left', 24)
        command('Resize shape to text')
        shot('paragraph-layout-fit')
        command('Slide size Standard'); attr('data-slide-width', 960)
        attr('data-text-margin-left', 18); attr('data-paragraph-after', 18)
        command('Undo'); attr('data-slide-width', 1280)
        attr('data-text-margin-left', 24); attr('data-paragraph-after', 24)
        command('Close format pane')
        page.set_viewport_size({'width': 720, 'height': 620})
        page.wait_for_function('() => JSON.parse(document.documentElement.getAttribute("data-ui-chrome"))["title-bar"].width === 720')
        edit_left_margin(18)
        attr('data-primary-text-length', len(text))
        shot('paragraph-layout-compact')
        observations.append({'textLength': len(text), 'leftMargin': value('data-text-margin-left'), 'wrap': value('data-text-wrap'),
                             'before': value('data-paragraph-before'), 'after': value('data-paragraph-after'), 'alignment': value('data-paragraph-align')})
        assert not errors, errors
        print('PASS: paragraph sample/player, margins, wrapping, justification, spacing, native layout fields, mixed styles, fitting, resizing and undo/redo.', flush=True)
    except Exception:
        failures.append(traceback.format_exc())
        raise
    finally:
        try:
            shot('paragraph-layout-final')
        except Exception as error:
            errors.append('Final screenshot: ' + str(error))
        (out / 'browser-text-body.json').write_text(json.dumps({'errors': errors, 'failures': failures, 'observations': observations}, indent=2))
        context.close(); browser.close()
