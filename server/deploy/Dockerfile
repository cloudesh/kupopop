FROM mcr.microsoft.com/dotnet/runtime-deps:10.0-noble
WORKDIR /app
COPY --chmod=755 Kupopop.Server /app/Kupopop.Server
RUN mkdir -p /var/lib/kupopop && chown app:app /var/lib/kupopop
USER app
ENV KUPOPOP_ROOT=/var/lib/kupopop
ENTRYPOINT ["/app/Kupopop.Server", "--proxy"]
