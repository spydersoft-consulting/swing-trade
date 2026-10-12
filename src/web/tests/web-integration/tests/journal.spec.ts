import { expect, test } from "@playwright/test";
import { login, openScreen, watchConsole } from "./support";

test("a trader records a pass, adds a note and filters the log", async ({ page }) => {
  const consoleErrors = watchConsole(page);
  const marker = `e2e-${Date.now()}`;
  await login(page, "trader");
  await openScreen(page, "Journal");

  await page.getByRole("button", { name: "Record a pass" }).click();
  const dialog = page.getByRole("dialog", { name: "Record a pass" });
  await dialog.getByLabel("Why no trade?").fill(`No clean pullback this week ${marker}`);
  await dialog.getByRole("button", { name: "Record pass" }).click();
  await expect(dialog).toBeHidden();

  const entry = page.getByTestId("note").filter({ hasText: marker });
  await expect(entry).toBeVisible();
  await expect(entry).toContainText("Pass");

  await page.getByRole("button", { name: "Add note" }).click();
  const noteDialog = page.getByRole("dialog", { name: "Add a note" });
  await noteDialog.getByLabel("Kind").selectOption("Review");
  await noteDialog.getByLabel("Note", { exact: true }).fill(`Review of the week ${marker}`);
  await noteDialog.getByRole("button", { name: "Save note" }).click();
  await expect(page.getByTestId("note").filter({ hasText: `Review of the week ${marker}` })).toBeVisible();

  // The pass log filter keeps passes and drops reviews.
  await page.getByRole("group", { name: "Filter by kind" }).getByRole("button", { name: "Pass" }).click();
  await expect(page.getByTestId("note").filter({ hasText: `Review of the week ${marker}` })).toHaveCount(0);
  await expect(page.getByTestId("note").filter({ hasText: `No clean pullback this week ${marker}` })).toBeVisible();

  // The notes survive a reload: they come from the API, not the browser.
  await page.reload();
  await expect(page.getByTestId("note").filter({ hasText: marker }).first()).toBeVisible();
  expect(consoleErrors()).toEqual([]);
});

test("the weekly routine is a checklist with a time box per step, kept per week in this browser", async ({ page }) => {
  await login(page, "reader");
  await openScreen(page, "Journal");

  const routine = page.getByRole("region", { name: "Weekly routine" });
  const steps = [
    ["Review open trades", "10 min"],
    ["Read the watchlist signals", "15 min"],
    ["Inspect candidates on the chart for swing low and prior high", "15 min"],
    ["Size the trade", "10 min"],
    ["Log it and place orders", "10 min"],
  ] as const;
  for (const [label, minutes] of steps) {
    const item = routine.getByRole("listitem").filter({ hasText: label });
    await expect(item).toContainText(minutes);
    await expect(item.getByRole("checkbox")).not.toBeChecked();
  }
  await expect(page.getByTestId("routine-progress")).toHaveText("0 of 60 minutes done");

  await routine.getByRole("checkbox", { name: "Review open trades" }).check();
  await routine.getByRole("checkbox", { name: "Read the watchlist signals" }).check();
  await expect(page.getByTestId("routine-progress")).toHaveText("25 of 60 minutes done");

  // Each step links to the screen where it is done.
  await routine.getByRole("link", { name: "Sizer" }).click();
  await expect(page.getByRole("heading", { level: 1, name: "Sizer" })).toBeVisible();

  // Back (and after a full reload) the ticks are still there.
  await page.reload();
  await openScreen(page, "Journal");
  await expect(page.getByTestId("routine-progress")).toHaveText("25 of 60 minutes done");
  await expect(routine.getByRole("checkbox", { name: "Review open trades" })).toBeChecked();

  await routine.getByRole("button", { name: "Reset week" }).click();
  await expect(page.getByTestId("routine-progress")).toHaveText("0 of 60 minutes done");
});
