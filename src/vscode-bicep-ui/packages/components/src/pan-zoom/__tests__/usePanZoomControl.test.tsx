import { screen, waitFor } from "@testing-library/react";
import { describe, expect, it } from "vitest";
import { renderPanZoomHook } from "./utils";
import { usePanZoomControl } from "../usePanZoomControl";

describe("usePanZoomControl", () => {
  it("should zoom in", async () => {
    const { result } = renderPanZoomHook(() => usePanZoomControl());
    const { zoomIn } = result.current;

    zoomIn();

    await waitFor(() => {
      expect(screen.getByText("pan-zoom transform spy")).toHaveStyle({ transform: "translate(0px, 0px) scale(1.4)" });
    });
  });

  it("should zoom out", async () => {
    const { result } = renderPanZoomHook(() => usePanZoomControl());
    const { zoomOut } = result.current;

    zoomOut();

    await waitFor(() => {
      expect(screen.getByText("pan-zoom transform spy").style.transform).toMatch(/^translate\(0px, 0px\) scale\(0.71.+\)$/);
    });
  });

  it("should reset", async () => {
    const { result } = renderPanZoomHook(() => usePanZoomControl());
    const { zoomOut, reset } = result.current;

    zoomOut();
    reset();

    await waitFor(() => {
      expect(screen.getByText("pan-zoom transform spy")).toHaveStyle({ transform: "translate(0px, 0px) scale(1)" });
    });
  });
});