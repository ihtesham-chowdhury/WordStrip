// The two identities WordStrip's text service has on this machine.
//
// Generated once, on 2026-08-12, and never to be regenerated. A TIP's CLSID is written into the registry at
// registration time and into every user's selected-input-method list; changing it would orphan those entries
// on every machine the service had ever been registered on, leaving a dead input method behind that only
// manual registry editing removes.

#pragma once

#include <guiddef.h>

// {85418D7E-C008-4E1B-981B-0DC9586800CC}
DEFINE_GUID(CLSID_WordStripTextService,
    0x85418d7e, 0xc008, 0x4e1b, 0x98, 0x1b, 0x0d, 0xc9, 0x58, 0x68, 0x00, 0xcc);

// {312BED7F-33DF-49BD-87EE-3B6BF1E2C614}
// The language profile. Distinct from the CLSID because one text service may expose several profiles - one
// per language, typically. WordStrip has exactly one, for en-US, because there is exactly one dictionary.
DEFINE_GUID(GUID_WordStripProfile,
    0x312bed7f, 0x33df, 0x49bd, 0x87, 0xee, 0x3b, 0x6b, 0xf1, 0xe2, 0xc6, 0x14);

#define WORDSTRIP_TIP_DESCRIPTION L"WordStrip"

// en-US. The bundled dictionary and n-gram model are English only, so claiming any other language would be
// a lie told to the input-method picker.
#define WORDSTRIP_TIP_LANGID 0x0409
