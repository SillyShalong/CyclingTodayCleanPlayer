package main

import (
	"bufio"
	"context"
	"fmt"
	"os"
	"sort"
	"strconv"
	"strings"
	"time"

	"go2tv.app/go2tv/v2/devices"
)

func main() {
	duration := 5 * time.Second
	if len(os.Args) > 1 {
		if milliseconds, err := strconv.Atoi(os.Args[1]); err == nil && milliseconds >= 1000 && milliseconds <= 15000 {
			duration = time.Duration(milliseconds) * time.Millisecond
		}
	}

	ctx, cancel := context.WithTimeout(context.Background(), duration)
	defer cancel()
	devices.StartDiscovery(ctx)

	found := make(map[string]devices.Device)
	collect := func() {
		list, err := devices.LoadAllDevices()
		if err != nil {
			return
		}
		for _, device := range list {
			if device.IsAudioOnly || strings.TrimSpace(device.Addr) == "" {
				continue
			}
			found[strings.ToLower(strings.TrimSpace(device.Addr))] = device
		}
	}

	ticker := time.NewTicker(250 * time.Millisecond)
	defer ticker.Stop()
	for {
		collect()
		select {
		case <-ctx.Done():
			collect()
			emit(found)
			return
		case <-ticker.C:
		}
	}
}

func emit(found map[string]devices.Device) {
	list := make([]devices.Device, 0, len(found))
	for _, device := range found {
		list = append(list, device)
	}
	sort.Slice(list, func(i, j int) bool {
		if list[i].Type != list[j].Type {
			return list[i].Type < list[j].Type
		}
		if list[i].Name != list[j].Name {
			return list[i].Name < list[j].Name
		}
		return list[i].Addr < list[j].Addr
	})

	writer := bufio.NewWriter(os.Stdout)
	defer writer.Flush()
	for _, device := range list {
		fmt.Fprintf(writer, "%s\t%s\t%s\n", clean(device.Type), clean(device.Name), clean(device.Addr))
	}
}

func clean(value string) string {
	value = strings.TrimSpace(value)
	replacer := strings.NewReplacer("\t", " ", "\r", " ", "\n", " ")
	return replacer.Replace(value)
}
