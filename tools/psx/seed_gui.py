"""Windows-friendly desktop interface for the PlayStation seed builder."""

import os
from pathlib import Path
from queue import Empty, Queue
import subprocess
import sys
from threading import Thread
import tkinter as tk
from tkinter import filedialog, messagebox, ttk


def show_directory(path):
    if os.name == "nt":
        os.startfile(path)
    else:
        subprocess.Popen(["open" if sys.platform == "darwin" else "xdg-open", str(path)])


class SeedWindow:
    def __init__(self, root, build, default_output, new_seed, identify_game, sources, games):
        self.root = root
        self.build = build
        self.default_output = default_output
        self.new_seed = new_seed
        self.identify_game = identify_game
        self.sources = sources
        self.sequel = games[1]
        self.messages = Queue()
        self.result = None
        self.busy = False
        self.game = tk.StringVar(root, games[0])
        self.cue = tk.StringVar(root)
        self.folder = tk.StringVar(root)
        self.seed = tk.StringVar(root, new_seed())
        self.guarded = tk.BooleanVar(root, False)
        self.hero = tk.BooleanVar(root, False)
        self.support = tk.BooleanVar(root, True)
        self.spoiler = tk.BooleanVar(root, False)
        self.destination = tk.StringVar(root)
        self.status = tk.StringVar(root, "Choose an original disc image to get started.")

        root.title("Stars of Destiny • PlayStation Alpha")
        root.minsize(570, 0)
        root.resizable(True, False)
        panel = ttk.Frame(root, padding=(22, 18, 22, 20))
        panel.grid(sticky="nsew")
        panel.columnconfigure(1, weight=1)
        root.columnconfigure(0, weight=1)
        ttk.Label(panel, text="Stars of Destiny", font=("Segoe UI", 22, "bold")).grid(
            row=0, column=0, columnspan=3, sticky="w",
        )
        ttk.Label(panel, text="PlayStation seed randomizer · Early alpha").grid(
            row=1, column=0, columnspan=3, sticky="w", pady=(0, 16),
        )

        self.inputs = []
        ttk.Label(panel, text="Game").grid(row=2, column=0, sticky="w", padx=(0, 12), pady=4)
        self.game_picker = ttk.Combobox(panel, textvariable=self.game, values=games, state="readonly")
        self.game_picker.grid(row=2, column=1, sticky="ew", pady=4)
        self.game_picker.bind("<<ComboboxSelected>>", self.choose_game)
        self.add_entry(panel, 3, "Original disc", self.cue, "Browse…", self.choose_cue)
        self.add_entry(panel, 4, "Save in", self.folder, "Browse…", self.choose_folder)
        self.add_entry(panel, 5, "Seed", self.seed, "New Seed", self.roll_seed)

        options = ttk.Frame(panel)
        options.grid(row=6, column=0, columnspan=3, sticky="w", pady=(8, 10))
        self.combat_only = ttk.Label(
            options, text="Guarded combat shuffle (78 Stars)\nRiou and Jowy keep their original roles.\n"
                          "Some Suikoden II scenes still show the original sprites.",
        )
        self.combat_only.grid(row=0, column=0, sticky="w", pady=2)
        self.expanded_options = []
        for index, (label, variable) in enumerate((
            ("Guarded combat shuffle (75 Stars)", self.guarded),
            ("Shuffle the named hero", self.hero),
            ("Shuffle support stars", self.support),
            ("Save a spoiler log", self.spoiler),
        )):
            check = ttk.Checkbutton(options, text=label, variable=variable)
            check.grid(row=index, column=0, sticky="w", pady=2)
            self.inputs.append(check)
            if variable is self.guarded:
                self.combat_option = check
            elif variable is not self.spoiler:
                self.expanded_options.append(check)

        ttk.Label(panel, textvariable=self.destination, wraplength=520).grid(
            row=7, column=0, columnspan=3, sticky="w", pady=(0, 9),
        )
        self.bar = ttk.Progressbar(panel, mode="indeterminate")
        self.bar.grid(row=8, column=0, columnspan=3, sticky="ew")
        ttk.Label(panel, textvariable=self.status, wraplength=520).grid(
            row=9, column=0, columnspan=3, sticky="w", pady=(6, 14),
        )
        actions = ttk.Frame(panel)
        actions.grid(row=10, column=0, columnspan=3, sticky="e")
        self.open_output = ttk.Button(actions, text="Open Output Folder", command=self.open_folder, state="disabled")
        self.open_output.pack(side="left", padx=(0, 8))
        self.generate = ttk.Button(actions, text="Generate Seed", command=self.generate_seed)
        self.generate.pack(side="left")

        for value in (self.folder, self.seed, self.hero, self.support):
            value.trace_add("write", self.show_destination)
        self.cue.trace_add("write", self.change_image)
        self.guarded.trace_add("write", self.change_roster)
        self.choose_game()
        root.protocol("WM_DELETE_WINDOW", self.close)
        root.after(80, self.poll)

    def add_entry(self, panel, row, label, variable, button_text, action):
        ttk.Label(panel, text=label).grid(row=row, column=0, sticky="w", padx=(0, 12), pady=4)
        entry = ttk.Entry(panel, textvariable=variable)
        entry.grid(row=row, column=1, sticky="ew", pady=4)
        button = ttk.Button(panel, text=button_text, command=action)
        button.grid(row=row, column=2, sticky="ew", padx=(8, 0), pady=4)
        self.inputs.extend((entry, button))

    def choose_cue(self):
        selected = filedialog.askopenfilename(parent=self.root, title="Choose the original Suikoden disc",
                                              filetypes=[("Disc descriptors", "*.cue *.ccd"), ("All files", "*")])
        if selected:
            self.cue.set(selected)

    def choose_game(self, *_):
        source = self.sources.get(self.game.get())
        self.cue.set(str(source) if source else "")

    def change_image(self, *_):
        entry = self.cue.get().strip()
        if entry:
            try:
                self.game.set(self.identify_game(Path(entry).expanduser()))
            except (OSError, ValueError) as error:
                self.status.set(str(error))
            else:
                self.status.set("Ready to generate a seed.")
        else:
            self.status.set("Choose the original CUE or CCD to get started.")
        self.change_roster()

    def choose_folder(self):
        selected = filedialog.askdirectory(parent=self.root, title="Choose where to save seed folders")
        if selected:
            self.folder.set(selected)

    def roll_seed(self):
        self.seed.set(self.new_seed())

    def roster_settings(self):
        guarded = self.game.get() == self.sequel or self.guarded.get()
        return dict(guarded=guarded, shuffle_hero=not guarded and self.hero.get(),
                    shuffle_support=not guarded and self.support.get())

    def change_roster(self, *_):
        sequel = self.game.get() == self.sequel
        if sequel:
            self.combat_option.grid_remove()
            self.combat_only.grid()
        else:
            self.combat_only.grid_remove()
            self.combat_option.grid()
        for option in self.expanded_options:
            if sequel or self.guarded.get():
                option.grid_remove()
            else:
                option.grid()
        self.show_destination()

    def output_path(self):
        cue = Path(self.cue.get().strip()).expanduser()
        suggested = self.default_output(cue.resolve(), self.seed.get(), **self.roster_settings())
        parent = self.folder.get().strip()
        return Path(parent).expanduser() / suggested.name if parent else suggested

    def show_destination(self, *_):
        self.destination.set("")
        if not self.cue.get().strip():
            return
        try:
            output = self.output_path()
        except (OSError, ValueError):
            return
        self.destination.set(f"New seed folder  {output}")

    def generate_seed(self):
        cue_text = self.cue.get().strip()
        if not cue_text:
            self.status.set("Choose the original CUE or CCD before generating a seed.")
            return
        if not self.seed.get().strip():
            self.roll_seed()
        cue = Path(cue_text).expanduser()
        try:
            output = self.output_path()
        except (OSError, ValueError) as error:
            self.status.set(str(error))
            return
        seed = self.seed.get()
        settings = dict(**self.roster_settings(), spoiler=self.spoiler.get())
        self.result = None
        self.busy = True
        self.open_output.configure(state="disabled")
        self.set_editable(False)
        self.bar.start(14)

        def generate():
            try:
                result = self.build(cue, output, seed, **settings,
                                    progress=lambda value: self.messages.put(("progress", value)))
            except Exception as error:
                self.messages.put(("error", str(error)))
            else:
                self.messages.put(("result", result))

        Thread(target=generate).start()

    def set_editable(self, enabled):
        self.game_picker.configure(state="readonly" if enabled else "disabled")
        for widget in (*self.inputs, self.generate):
            widget.configure(state="normal" if enabled else "disabled")

    def poll(self):
        try:
            while True:
                kind, value = self.messages.get_nowait()
                if kind == "progress":
                    self.status.set(value)
                    continue
                self.busy = False
                self.bar.stop()
                self.set_editable(True)
                if kind == "error":
                    self.status.set(value)
                    messagebox.showerror("Seed generation failed", value, parent=self.root)
                else:
                    self.result = value
                    self.open_output.configure(state="normal")
                    guests = f" and {value.shuffled_guests} guests" if value.shuffled_guests else ""
                    self.status.set(f"Seed ready. Shuffled {value.shuffled_stars} stars{guests}."
                                    " Open the generated CUE in your emulator.")
        except Empty:
            pass
        self.root.after(80, self.poll)

    def open_folder(self):
        if self.result is not None:
            show_directory(self.result.cue.parent)

    def close(self):
        if not self.busy:
            self.root.destroy()


def launch(build, default_output, new_seed, identify_game, sources, games):
    root = tk.Tk()
    SeedWindow(root, build, default_output, new_seed, identify_game, sources, games)
    root.mainloop()
