"""Configuracion centralizada de structlog."""

from __future__ import annotations

import logging
import sys

import structlog


class _StderrLoggerFactory:
    """Factory que siempre resuelve sys.stderr en el momento del log.

    PrintLoggerFactory captura la referencia al file en configure-time,
    lo que rompe en pytest (que reemplaza stderr para capturarlo).
    """

    def __call__(self, *args, **kwargs) -> structlog.PrintLogger:
        return structlog.PrintLogger(file=sys.stderr)


def setup(debug: bool = False) -> None:
    """Configura structlog para la app.

    En modo debug loguea en consola con colores legibles.
    Sin debug loguea en JSON para consumo programatico.
    """
    level = logging.DEBUG if debug else logging.INFO

    shared_processors: list[structlog.types.Processor] = [
        structlog.contextvars.merge_contextvars,
        structlog.processors.add_log_level,
        structlog.processors.TimeStamper(fmt="iso"),
    ]

    if debug:
        renderer: structlog.types.Processor = structlog.dev.ConsoleRenderer()
    else:
        renderer = structlog.processors.JSONRenderer(ensure_ascii=False)

    structlog.configure(
        processors=[
            *shared_processors,
            structlog.processors.StackInfoRenderer(),
            structlog.processors.format_exc_info,
            renderer,
        ],
        wrapper_class=structlog.make_filtering_bound_logger(level),
        context_class=dict,
        logger_factory=_StderrLoggerFactory(),
        cache_logger_on_first_use=False,
    )
