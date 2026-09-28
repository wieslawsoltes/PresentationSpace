"""Real-input shell sizing and table-design regressions. Diagnostics are read-only arranged geometry."""
import json
import io
import traceback
from PIL import Image
import os
from pathlib import Path
from playwright.sync_api import sync_playwright

out = Path('artifacts/screenshots')
out.mkdir(parents=True, exist_ok=True)
url = os.environ.get('PRESENTATIONSPACE_URL', 'http://127.0.0.1:8080/PresentationSpace/')
errors, observations, failures = [], [], []
with sync_playwright() as p:
    browser = p.chromium.launch(args=['--no-sandbox', '--use-angle=swiftshader', '--enable-unsafe-swiftshader'])
    context = browser.new_context(viewport={'width': 1440, 'height': 1000})
    page = context.new_page()
    page.on('pageerror', lambda error: errors.append(str(error)))
    def settle():
        page.evaluate('() => new Promise(resolve => requestAnimationFrame(() => requestAnimationFrame(resolve)))')
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
        settle()
    def chrome():
        return json.loads(value('data-ui-chrome'))
    def wait_layout(width, height):
        # A pair of JS animation frames is not a completion fence for Uno's XAML
        # dispatcher. Wait for the actual arranged root before inspecting children.
        page.wait_for_function("""([width, height]) => {
            const raw = document.documentElement.getAttribute('data-ui-chrome');
            if (!raw) return false;
            const layout = JSON.parse(raw), title = layout['title-bar'];
            const status = layout['status-bar'], workspace = layout['workspace'];
            return Math.abs(title.width - width) < 1 && Math.abs(workspace.width - width) < 1
                && status.height > 0 && Math.abs(status.y + status.height - height) < 1;
        }""", arg=[width, height], timeout=20000)
        settle()
    def wait_pane(width):
        # Keep all clipping checks, but do not assert against the command's
        # pre-arrange snapshot (visible=true with zero size and the old position).
        page.wait_for_function("""width => {
            const raw = document.documentElement.getAttribute('data-ui-chrome');
            if (!raw) return false;
            const layout = JSON.parse(raw), pane = layout['format-pane'];
            return Math.abs(layout['title-bar'].width - width) < 1 && pane.visible
                && pane.width >= 296 && pane.height > 0 && pane.x >= 0
                && pane.x + pane.width <= width + .5;
        }""", arg=width, timeout=20000)
        return chrome()['format-pane']
    def click(name):
        rect = chrome()[name]
        assert rect['visible'], (name, rect)
        page.mouse.click(rect['x'] + rect['width'] / 2, rect['y'] + rect['height'] / 2)
    def check_switch_pixels():
        # Bounds alone would miss a theme painting a rectangular checked background.
        # Sample the hit-target padding, away from the pill and the outer focus ring.
        layout = chrome()
        rect = layout['autosave-switch']
        page.mouse.move(300, 120)
        settle()
        image = Image.open(io.BytesIO(page.screenshot())).convert('RGB')
        scale = page.evaluate('window.devicePixelRatio')
        def pixel(x, y):
            return image.getpixel((round(x * scale), round(y * scale)))
        background = pixel(2, 22)
        for y in [rect['y'] + 3, rect['y'] + rect['height'] - 4]:
            sample = pixel(rect['x'] + rect['width'] / 2, y)
            assert max(abs(a-b) for a, b in zip(background, sample)) <= 4, (background, sample, rect)
    def check_layout(width):
        layout = chrome()
        order = ['title-logo','autosave-label','autosave-switch','quick-save','quick-undo','quick-redo','document-title','command-search','quick-comments','quick-present','quick-share','quick-more']
        previous = None
        for name in order:
            rect = layout[name]
            if not rect['visible']:
                continue
            assert rect['width'] > 0 and rect['height'] > 0, (name, rect)
            assert rect['x'] >= -.5 and rect['x'] + rect['width'] <= width + .5, (width, name, rect)
            assert rect['y'] >= -.5 and rect['y'] + rect['height'] <= 44.5, (width, name, rect)
            assert abs(rect['y'] + rect['height']/2 - 22) < 1.1, (width, name, rect)
            if previous is not None:
                assert previous[1]['x'] + previous[1]['width'] <= rect['x'] + .5, (width, previous, name, rect)
            previous = (name, rect)
        track, thumb = layout['autosave-track'], layout['autosave-thumb']
        assert abs(track['height'] - 20) < .1, track
        assert track['y'] >= 6 and track['y'] + track['height'] <= 38, track
        assert thumb['x'] >= track['x'] and thumb['x'] + thumb['width'] <= track['x'] + track['width'], (track, thumb)
        assert abs((thumb['y'] + thumb['height']/2) - (track['y'] + track['height']/2)) < .1
        observations.append({'width': width, 'layout': layout})
    try:
        page.goto(url, wait_until='domcontentloaded', timeout=60000)
        page.wait_for_function("document.documentElement.getAttribute('data-presentationspace') === 'ready'", timeout=120000)
        wait_layout(1440, 1000)
        click('autosave-switch')
        attr('data-autosave', 'false')
        page.keyboard.press('Space')
        attr('data-autosave', 'true')
        check_switch_pixels()
        # Exercise the real rename dialog with a maximum-length title before resizing.
        click('document-title')
        page.wait_for_function("() => document.activeElement?.id === 'uno-input' && document.activeElement.tagName === 'INPUT'")
        page.keyboard.press('Control+a')
        long_title = ('Quarterly presentation with a deliberately long document title ' * 3)[:120]
        page.keyboard.type(long_title)
        page.keyboard.press('Enter')
        page.wait_for_function('(title) => document.title.startsWith(title)', arg=long_title)
        settle()
        page.screenshot(path=str(out / 'chrome-desktop.png'))
        for width in [1920, 1440, 1280, 1180, 1100, 1024, 1000, 900, 820, 720, 640, 520, 390, 320]:
            page.set_viewport_size({'width': width, 'height': 844 if width < 640 else 1000})
            wait_layout(width, 844 if width < 640 else 1000)
            check_layout(width)
            if width in [1024, 390, 320]:
                command('Show Insert ribbon')
                attr('data-ribbon-tab', 'Insert')
                command('Show Home ribbon')
                page.screenshot(path=str(out / f'chrome-{width}.png'))
        # The compact ribbon is reachable with real pointer input, not only command search.
        attr('data-ribbon-groups-overflow', 'true')
        before = float(value('data-ribbon-offset'))
        ribbon = chrome()['ribbon']
        page.mouse.click(307, ribbon['y'] + 34 + 45)
        page.wait_for_function('(before) => Number(document.documentElement.getAttribute("data-ribbon-offset")) > before', arg=before)
        settle()
        page.screenshot(path=str(out / 'chrome-ribbon-scrolled.png'))
        # Search must remain usable when the title-bar search is collapsed.
        command('New slide')
        attr('data-slide-count', 5)
        command('Undo')
        attr('data-slide-count', 4)
        print('PASS: switch pointer/keyboard states, centered unclipped chrome at 14 widths, compact command search.', flush=True)
        page.set_viewport_size({'width': 1440, 'height': 1000})
        wait_layout(1440, 1000)
        command('New slide')
        page.keyboard.press('Control+a'); page.keyboard.press('Delete')
        command('Insert table')
        command('Table select all')
        command('Table style Blue')
        attr('data-table-accent', '#285EA8')
        command('Table toggle first column')
        attr('data-table-first-column', 'true')
        command('Undo'); attr('data-table-first-column', 'false')
        command('Redo'); attr('data-table-first-column', 'true')
        command('Table toggle last column'); attr('data-table-last-column', 'true')
        command('Table toggle banded columns'); attr('data-table-banded-columns', 'true')
        origins = int(value('data-table-origins'))
        rows, columns = int(value('data-table-rows')), int(value('data-table-columns'))
        command('Table borders None'); attr('data-table-zero-borders', origins * 4)
        command('Table borders Outside'); attr('data-table-zero-borders', origins * 4 - rows * 2 - columns * 2)
        command('Undo'); attr('data-table-zero-borders', origins * 4)
        command('Redo'); attr('data-table-zero-borders', origins * 4 - rows * 2 - columns * 2)
        command('Show Table Design ribbon')
        attr('data-ribbon-tab', 'Table Design')
        page.screenshot(path=str(out / 'table-design-06.png'))
        print('PASS: table palettes, first/last/banded columns, selective borders and undo/redo through real commands.', flush=True)
        command('Close format pane')
        page.set_viewport_size({'width': 390, 'height': 500})
        wait_layout(390, 500)
        command('Format shape')
        pane = wait_pane(390)
        assert pane['visible'] and pane['x'] >= 0 and pane['x'] + pane['width'] <= 390.5, pane
        command('Toggle slide thumbnails')
        pane = wait_pane(390)
        assert pane['width'] >= 296 and pane['x'] >= 0 and pane['x'] + pane['width'] <= 390.5, pane
        page.screenshot(path=str(out / 'chrome-compact-inspector.png'))
        command('Close format pane')
        # Fresh high-DPI context also exercises the startup layout at compact, low height.
        context.close()
        context = browser.new_context(viewport={'width': 720, 'height': 500}, device_scale_factor=2)
        page = context.new_page()
        page.on('pageerror', lambda error: errors.append(str(error)))
        page.goto(url, wait_until='domcontentloaded', timeout=60000)
        page.wait_for_function("document.documentElement.getAttribute('data-presentationspace') === 'ready'", timeout=120000)
        wait_layout(720, 500)
        check_layout(720)
        click('autosave-switch'); attr('data-autosave', 'false')
        page.keyboard.press('Space'); attr('data-autosave', 'true')
        check_switch_pixels()
        for name in ['workspace', 'status-bar']:
            rect = chrome()[name]
            assert rect['y'] + rect['height'] <= 500.5, (name, rect)
        page.screenshot(path=str(out / 'chrome-hidpi.png'))
        print('PASS: long titles, pointer ribbon scrolling, compact inspector and 2x-scale low-height startup.', flush=True)
        assert not errors, errors
    except Exception:
        failures.append(traceback.format_exc())
        raise
    finally:
        try:
            page.screenshot(path=str(out / 'chrome-final.png'))
        except Exception as error:
            errors.append('Final screenshot: ' + str(error))
        (out / 'browser-chrome.json').write_text(json.dumps({'observations': observations, 'errors': errors, 'failures': failures, 'lastLayoutVersion': value('data-ui-layout-version')}, indent=2))
        context.close(); browser.close()
