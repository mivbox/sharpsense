import assert from "node:assert/strict";
import type { Page } from "puppeteer";

export async function clickButton(page: Page, text: string) {
  const handle = await page.waitForFunction(
    (label) =>
      [
        ...(
          document.querySelector('[role="dialog"]') ?? document
        ).querySelectorAll("button"),
      ].find(
        (button) =>
          !button.disabled &&
          button.getClientRects().length > 0 &&
          button.innerText.replace(/\u200b/g, "").trim() === label,
      ),
    {},
    text,
  );
  try {
    const element = handle.asElement();
    assert.ok(element, `Button not found: ${text}`);
    const button = await element.toElement("button");
    await button.scrollIntoView();
    await page.waitForFunction(
      (target) => {
        const bounds = target.getBoundingClientRect();
        const hit = document.elementFromPoint(
          bounds.x + bounds.width / 2,
          bounds.y + bounds.height / 2,
        );
        return hit !== null && target.contains(hit);
      },
      {},
      button,
    );
    await button.asLocator().click();
  } finally {
    await handle.dispose();
  }
}
