# ADR 0002: Bootstrap rendering with SDL_GPU and SDL_shadercross

## Status

Accepted for the Windows-first v0.1 implementation.

## Context

Obelisk needs a modern GPU path that remains behind the platform boundary. The
first rendering milestone must prove device creation, swapchain presentation,
shader compilation, pipeline creation, clear, drawing, and deterministic
resource teardown without exposing SDL types to game code.

SDL_GPU supports D3D12, Vulkan, and Metal, but each backend consumes different
shader formats. Checking precompiled backend-specific shader binaries into the
engine at this stage would either lock the prototype to one driver or require a
shader build toolchain before the asset pipeline exists.

## Decision

- Use SDL_GPU for device, swapchain, command buffer, render pass, and pipeline
  management.
- Use `SDL_shadercross` 3.0.0.12 to compile embedded HLSL to SPIR-V and then to
  the format selected by the active SDL_GPU backend.
- Generate the bootstrap rectangle from `SV_VertexID`, so the technical spike
  does not require vertex or index buffers.
- Clear the swapchain as the load operation of the render pass, then draw six
  vertices for one rectangle and submit the command buffer.
- Keep the bootstrap shader and fixed rectangle private to the SDL3 backend.
  Day 6 will replace the fixed draw with data supplied through an engine-owned
  rendering contract.
- Release the pipeline and window claim before destroying the GPU device, then
  destroy the window and shut down shadercross and SDL in reverse order.

## Consequences

- The engine proves a real GPU frame without adding an external shader compiler
  installation requirement.
- Runtime shader compilation is acceptable for the bootstrap, but production
  asset builds should compile and cache shaders ahead of time.
- SDL GPU types remain isolated in `Obelisk.Platform.Sdl3`.
- The current native runtime package is Windows-specific; platform package
  selection must be generalized before non-Windows distribution.

## References

- <https://wiki.libsdl.org/SDL3/CategoryGPU>
- <https://wiki.libsdl.org/SDL3/SDL_ClaimWindowForGPUDevice>
- <https://wiki.libsdl.org/SDL3/SDL_BeginGPURenderPass>
